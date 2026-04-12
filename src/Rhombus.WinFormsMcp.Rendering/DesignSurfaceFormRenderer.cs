using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rhombus.WinFormsMcp.Rendering;

/// <summary>
/// Renders WinForms .Designer.cs files to PNG images using
/// System.ComponentModel.Design.DesignSurface — the same infrastructure
/// Visual Studio uses for its WinForms designer.
///
/// Combines Roslyn syntax-tree parsing with DesignSurface + IDesignerHost
/// for control creation, giving VS-parity rendering.
///
/// Supports standard controls, custom controls (loaded from project build
/// output), UserControls, and the full property-assignment spectrum from
/// .Designer.cs files.
/// </summary>
public class DesignSurfaceFormRenderer {
    private static bool _visualStylesInitialized;
    private readonly Dictionary<string, byte[]> _cache = [];
    private readonly Dictionary<string, Type?> _typeCache = [];

    // Per-render state
    private Dictionary<string, IComponent> _components = [];
    private Control? _rootControl;

    private IDesignerHost? _host;
    private List<Assembly> _extraAssemblies = [];

    // Recursive rendering: project directory for searching .Designer.cs files
    private string? _projectDir;

    // Circular reference protection for recursive UserControl rendering (shared across threads)
    private static readonly HashSet<string> RenderingStack = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object RenderingStackLock = new();

    private static void EnsureVisualStyles() {
        if (_visualStylesInitialized)
            return;
        try {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
        catch {
            // Already initialized by host process
        }
        _visualStylesInitialized = true;
    }

    /// <summary>
    /// Render a .Designer.cs file from its file path.
    /// Reads the companion .cs file (if present) to detect Form vs UserControl.
    /// Scans the project bin/ directory for custom control assemblies.
    /// </summary>
    public byte[] RenderForm(string sourceFilePath) {
        var designerFile = FormRenderingHelpers.ResolveDesignerFile(sourceFilePath);
        var designerContent = File.ReadAllText(designerFile);

        // Try to find companion .cs for base type detection
        var idx = designerFile.LastIndexOf(".Designer.cs", StringComparison.OrdinalIgnoreCase);
        var companionPath = idx >= 0 ? designerFile.Substring(0, idx) + ".cs" : designerFile;
        string? companionContent = null;
        if (File.Exists(companionPath) && !companionPath.Equals(designerFile, StringComparison.OrdinalIgnoreCase))
            companionContent = File.ReadAllText(companionPath);

        // Resolve project assemblies for custom controls
        var projectDir = Path.GetDirectoryName(designerFile)!;
        var extraPaths = ResolveProjectAssemblyPaths(projectDir);

        return RenderDesignerCode(designerContent, companionContent, extraPaths, projectDir);
    }

    /// <summary>
    /// Render designer code from a string, with optional companion content and extra assemblies.
    /// </summary>
    public byte[] RenderDesignerCode(string designerContent, string? companionContent = null,
        IEnumerable<string>? extraAssemblyPaths = null, string? projectDir = null) {
        var cacheKey = ComputeHash(designerContent + (companionContent ?? ""));
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        EnsureVisualStyles();

        // Store project directory for recursive UserControl rendering
        _projectDir = projectDir;

        // Load extra assemblies into runtime
        _extraAssemblies = [];
        if (extraAssemblyPaths != null) {
            foreach (var path in extraAssemblyPaths) {
                if (!File.Exists(path))
                    continue;
                try {
                    _extraAssemblies.Add(Assembly.LoadFrom(path));
                }
                catch { /* skip unloadable DLLs */ }
            }
        }

        // Detect base type from companion file
        var baseType = DetectBaseType(designerContent, companionContent);

        // Parse InitializeComponent
        var tree = CSharpSyntaxTree.ParseText(designerContent);
        var root = tree.GetCompilationUnitRoot();
        var initMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "InitializeComponent");

        if (initMethod?.Body == null)
            throw new InvalidOperationException("InitializeComponent() method not found in designer code.");

        // Run DesignSurface operations on STA thread
        byte[]? pngBytes = null;
        Exception? threadException = null;

        var thread = new Thread(() => {
            try {
                pngBytes = RenderOnStaThread(baseType, initMethod.Body.Statements);
            }
            catch (Exception ex) {
                threadException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(timeout: TimeSpan.FromMilliseconds(10000));

        if (threadException != null)
            throw new InvalidOperationException(
                $"DesignSurface render failed: {threadException.Message}", threadException);

        if (pngBytes == null)
            throw new InvalidOperationException("DesignSurface render failed: no output produced.");

        _cache[cacheKey] = pngBytes;
        return pngBytes;
    }

    /// <summary>
    /// Render a .Designer.cs file to PNG. Backward-compatible signature for the render_form tool.
    /// </summary>
    public byte[] RenderDesignerFile(string designerFilePath, string? outputPath = null) {
        var pngBytes = RenderForm(designerFilePath);

        if (!string.IsNullOrEmpty(outputPath)) {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputPath, pngBytes);
        }

        return pngBytes;
    }

    private byte[] RenderOnStaThread(Type baseType, SyntaxList<StatementSyntax> statements) {
        using var surface = new DesignSurface();
        surface.BeginLoad(baseType);

        if (!surface.IsLoaded) {
            var errors = surface.LoadErrors?
                .Cast<object>()
                .Select(e => e.ToString()) ?? Array.Empty<string>();
            throw new InvalidOperationException(
                $"DesignSurface failed to load {baseType.Name}: {string.Join("; ", errors)}");
        }

        _host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        _rootControl = (Control)_host.RootComponent;
        _components = [];

        // Execute each statement from InitializeComponent
        foreach (var statement in statements) {
            try {
                ExecuteStatement(statement);
            }
            catch {
                // Graceful degradation: skip statements that can't be executed
            }
        }

        // Render
        var view = (Control)surface.View;

        var width = _rootControl.Width > 0 ? _rootControl.Width : 300;
        var height = _rootControl.Height > 0 ? _rootControl.Height : 200;

        // For UserControls, prefer Size; for Forms, prefer ClientSize
        if (_rootControl is Form form) {
            width = form.ClientSize.Width > 0 ? form.ClientSize.Width : width;
            height = form.ClientSize.Height > 0 ? form.ClientSize.Height : height;
        }

        width = Math.Max(width, 100);
        height = Math.Max(height, 100);

        // Disable scrollbars — they are meaningless in a static preview
        // and cause gutter space / scrollbar arrows in the rendered image.
        if (_rootControl is Form chromeForm)
            chromeForm.AutoScroll = false;
        if (view is ScrollableControl scrollableView)
            scrollableView.AutoScroll = false;

        // The DesignSurface view paints form chrome (title bar, borders)
        // inside its client area. Make the view oversized so the
        // DesignSurface never shows scrollbars, then crop to exact bounds.
        int formW, formH;
        if (_rootControl is Form sizedForm) {
            if (!sizedForm.IsHandleCreated)
                _ = sizedForm.Handle;
            formW = sizedForm.Size.Width;
            formH = sizedForm.Size.Height;
            // Oversize the view to guarantee no scrollbars appear
            view.ClientSize = new Size(formW + 100, formH + 100);
        }
        else {
            formW = width;
            formH = height;
            view.ClientSize = new Size(width, height);
        }

        // Force handle creation
        if (!view.IsHandleCreated) {
            _ = view.Handle;
        }

        // Render the oversized view to bitmap
        var bmpWidth = view.ClientSize.Width;
        var bmpHeight = view.ClientSize.Height;
        using var fullBitmap = new Bitmap(bmpWidth, bmpHeight);
        view.DrawToBitmap(fullBitmap, new Rectangle(0, 0, bmpWidth, bmpHeight));

        // Crop to just the form. The DesignSurface places the form at a
        // small offset (typically 1px) from the view's origin.
        Bitmap bitmap;
        if (_rootControl is Form) {
            // Scan for the form's top-left corner: find the first non-background pixel
            var bgColor = fullBitmap.GetPixel(0, 0);
            int cropX = 0, cropY = 0;
            for (int x = 0; x < Math.Min(20, bmpWidth); x++) {
                if (fullBitmap.GetPixel(x, Math.Min(10, bmpHeight - 1)) != bgColor) {
                    cropX = x;
                    break;
                }
            }
            for (int y = 0; y < Math.Min(20, bmpHeight); y++) {
                if (fullBitmap.GetPixel(Math.Min(10, bmpWidth - 1), y) != bgColor) {
                    cropY = y;
                    break;
                }
            }
            int cropW = Math.Min(formW, bmpWidth - cropX);
            int cropH = Math.Min(formH, bmpHeight - cropY);
            bitmap = fullBitmap.Clone(
                new Rectangle(cropX, cropY, cropW, cropH),
                fullBitmap.PixelFormat);
        }
        else {
            bitmap = (Bitmap)fullBitmap.Clone();
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        bitmap.Dispose();
        return ms.ToArray();
    }

    #region Statement Execution

    internal void ExecuteStatement(StatementSyntax statement) {
        if (statement is ExpressionStatementSyntax exprStmt) {
            ExecuteExpressionStatement(exprStmt.Expression);
        }
    }

    private void ExecuteExpressionStatement(ExpressionSyntax expression) {
        if (expression is AssignmentExpressionSyntax assignment) {
            ExecuteAssignment(assignment);
        }
        else if (expression is InvocationExpressionSyntax invocation) {
            ExecuteInvocation(invocation);
        }
    }

    private void ExecuteAssignment(AssignmentExpressionSyntax assignment) {
        // Skip event wireups (+=)
        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression))
            return;

        var left = assignment.Left;
        var right = assignment.Right;

        // Field assignment: this.button1 = new System.Windows.Forms.Button();
        if (right is ObjectCreationExpressionSyntax creation) {
            var fieldName = GetFieldName(left);

            if (fieldName != null) {
                var typeName = creation.Type.ToString();
                var type = ResolveType(typeName);
                if (type == null) {
                    // Try recursive rendering from source .Designer.cs
                    var recursivePlaceholder = TryRenderUserControlFromSource(typeName);
                    if (recursivePlaceholder != null) {
                        _components[fieldName] = recursivePlaceholder;
                        return;
                    }

                    // Unknown type — create a placeholder so the layout stays intact
                    var placeholder = CreateErrorPlaceholder(typeName, "Type not found");
                    _components[fieldName] = placeholder;
                    return;
                }

                // Special case: skip IContainer (components)
                if (typeof(IContainer).IsAssignableFrom(type)) {
                    try {
                        var instance = Activator.CreateInstance(type);
                        if (instance is IComponent comp)
                            _components[fieldName] = comp;
                    }
                    catch { /* skip */ }
                    return;
                }

                // Create component via DesignSurface host if it's a Component type
                if (typeof(IComponent).IsAssignableFrom(type) && _host != null) {
                    try {
                        var component = _host.CreateComponent(type, fieldName);
                        _components[fieldName] = component;

                        // If it's a property on the root (e.g., this.ClientSize = new Size(...))
                        // check if fieldName matches a property on root
                        var rootProp = _rootControl?.GetType().GetProperty(fieldName,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (rootProp != null && rootProp.CanWrite && component is not Control) {
                            SetPropertyValue(_rootControl!, fieldName, component);
                        }
                    }
                    catch (Exception ex) {
                        // Fall back to direct instantiation
                        try {
                            var args = EvaluateArgumentList(creation.ArgumentList);
                            var instance = args.Length > 0
                                ? Activator.CreateInstance(type, args)
                                : Activator.CreateInstance(type);
                            if (instance is IComponent comp)
                                _components[fieldName] = comp;
                        }
                        catch {
                            // Both paths failed — show error placeholder for controls
                            if (typeof(Control).IsAssignableFrom(type)) {
                                var placeholder = CreateErrorPlaceholder(typeName, ex.Message);
                                _components[fieldName] = placeholder;
                            }
                        }
                    }
                    return;
                }

                // Non-component: it might be a property assignment disguised as field assignment
                // e.g., this.ClientSize = new Size(400, 300)
                try {
                    var rootProp = _rootControl?.GetType().GetProperty(fieldName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (rootProp != null && rootProp.CanWrite) {
                        var args = EvaluateArgumentList(creation.ArgumentList);
                        if (args.Any(a => a is EvaluationSkipped))
                            return;
                        var value = args.Length > 0
                            ? Activator.CreateInstance(type, args)
                            : Activator.CreateInstance(type);
                        SetPropertyValue(_rootControl!, fieldName, value);
                        return;
                    }

                    // Store as non-component value
                    var inst = Activator.CreateInstance(type,
                        EvaluateArgumentList(creation.ArgumentList));
                    // We can't store non-IComponent in _components, but some statements
                    // may reference it. Use a separate lookup would be needed, but for now skip.
                }
                catch { /* skip */ }

                return;
            }
            // fieldName is null: this could be a nested property assignment
            // Fall through to property-setting logic below
        }

        // Property assignment: this.button1.Text = "Click";
        var target = ResolveTarget(left, out var propertyName);
        if (target == null || propertyName == null)
            return;

        var value2 = EvaluateExpression(right);
        if (value2 is EvaluationSkipped)
            return;

        SetPropertyValue(target, propertyName, value2);
    }

    private void ExecuteInvocation(InvocationExpressionSyntax invocation) {
        var expr = invocation.Expression;

        if (expr is MemberAccessExpressionSyntax memberAccess) {
            var methodName = memberAccess.Name.Identifier.Text;

            // Handle Controls.Add(this.button1)
            if (methodName == "Add" && memberAccess.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Controls" } parentAccess) {
                var parentObj = ResolveObjectExpression(parentAccess.Expression);
                if (parentObj is Control parentControl && invocation.ArgumentList.Arguments.Count == 1) {
                    var childExpr = invocation.ArgumentList.Arguments[0].Expression;
                    var childObj = EvaluateExpression(childExpr);
                    if (childObj is Control childControl) {
                        childControl.Parent = parentControl;
                    }
                }
                return;
            }

            // Handle Controls.AddRange(...)
            if (methodName == "AddRange" && memberAccess.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Controls" } controlsAccess) {
                var parentObj = ResolveObjectExpression(controlsAccess.Expression);
                if (parentObj is Control parentCtrl && invocation.ArgumentList.Arguments.Count == 1) {
                    var argValue = EvaluateExpression(invocation.ArgumentList.Arguments[0].Expression);
                    if (argValue is Control[] controls) {
                        foreach (var c in controls)
                            c.Parent = parentCtrl;
                    }
                    else if (argValue is object[] objs) {
                        foreach (var c in objs.OfType<Control>())
                            c.Parent = parentCtrl;
                    }
                }
                return;
            }

            // Handle Items.AddRange(...)
            if (methodName == "AddRange" && memberAccess.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Items" } itemsAccess) {
                var targetObj = ResolveObjectExpression(itemsAccess.Expression);
                if (targetObj != null && invocation.ArgumentList.Arguments.Count == 1) {
                    var argValue = EvaluateExpression(invocation.ArgumentList.Arguments[0].Expression);
                    if (argValue is object[] items) {
                        var itemsProp = targetObj.GetType().GetProperty("Items",
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            ?? targetObj.GetType().GetProperty("Items",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (itemsProp != null) {
                            var itemsCollection = itemsProp.GetValue(targetObj);
                            if (itemsCollection is System.Collections.IList list) {
                                foreach (var item in items) {
                                    if (item != null)
                                        list.Add(item);
                                }
                            }
                        }
                    }
                }
                return;
            }

            // Handle TabPages.Add(...)
            if (methodName == "Add" && memberAccess.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "TabPages" } tabPagesAccess) {
                var targetObj = ResolveObjectExpression(tabPagesAccess.Expression);
                if (targetObj is TabControl tabControl && invocation.ArgumentList.Arguments.Count == 1) {
                    var childObj = EvaluateExpression(invocation.ArgumentList.Arguments[0].Expression);
                    if (childObj is TabPage tabPage) {
                        tabControl.TabPages.Add(tabPage);
                    }
                }
                return;
            }

            // Handle SuspendLayout(), ResumeLayout(), PerformLayout()
            if (methodName is "SuspendLayout" or "ResumeLayout" or "PerformLayout") {
                var targetObj = ResolveObjectExpression(memberAccess.Expression);
                if (targetObj is Control control) {
                    if (methodName == "SuspendLayout")
                        control.SuspendLayout();
                    else if (methodName == "PerformLayout")
                        control.PerformLayout();
                    else if (methodName == "ResumeLayout") {
                        if (invocation.ArgumentList.Arguments.Count == 1) {
                            var arg = EvaluateExpression(invocation.ArgumentList.Arguments[0].Expression);
                            if (arg is bool boolArg)
                                control.ResumeLayout(boolArg);
                            else
                                control.ResumeLayout();
                        }
                        else {
                            control.ResumeLayout();
                        }
                    }
                }
                return;
            }

            // Handle BeginInit() / EndInit() — e.g., ((ISupportInitialize)(this.nudLevel)).BeginInit()
            if (methodName is "BeginInit" or "EndInit") {
                // These are often cast expressions; we can safely skip them for rendering
                return;
            }
        }
    }

    #endregion

    #region Expression Evaluation

    internal object? EvaluateExpression(ExpressionSyntax expression) {
        return expression switch {
            LiteralExpressionSyntax literal => literal.Token.Value,
            ObjectCreationExpressionSyntax creation => EvaluateObjectCreation(creation),
            MemberAccessExpressionSyntax memberAccess => EvaluateMemberAccess(memberAccess),
            InvocationExpressionSyntax invocation => EvaluateInvocationExpression(invocation),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.BitwiseOrExpression) => EvaluateBitwiseOr(binary),
            CastExpressionSyntax cast => EvaluateExpression(cast.Expression),
            ParenthesizedExpressionSyntax paren => EvaluateExpression(paren.Expression),
            ArrayCreationExpressionSyntax array => EvaluateArrayCreation(array),
            ImplicitArrayCreationExpressionSyntax implicitArray => EvaluateImplicitArrayCreation(implicitArray),
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.UnaryMinusExpression) => EvaluateNegation(prefix),
            IdentifierNameSyntax identifier => ResolveIdentifier(identifier.Identifier.Text),
            ThisExpressionSyntax => _rootControl,
            _ => EvaluationSkipped.Instance,
        };
    }

    private object? EvaluateObjectCreation(ObjectCreationExpressionSyntax creation) {
        var typeName = creation.Type.ToString();
        var type = ResolveType(typeName);
        if (type == null)
            return EvaluationSkipped.Instance;

        var args = EvaluateArgumentList(creation.ArgumentList);
        if (args.Any(a => a is EvaluationSkipped))
            return EvaluationSkipped.Instance;

        try {
            if (args.Length > 0)
                return Activator.CreateInstance(type, args);
            return Activator.CreateInstance(type);
        }
        catch {
            return EvaluationSkipped.Instance;
        }
    }

    private object? EvaluateMemberAccess(MemberAccessExpressionSyntax memberAccess) {
        var memberName = memberAccess.Name.Identifier.Text;

        // Handle this.field references
        if (memberAccess.Expression is ThisExpressionSyntax) {
            if (_components.TryGetValue(memberName, out var comp))
                return comp;
            // Might be a property on the root
            var rootProp = _rootControl?.GetType().GetProperty(memberName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (rootProp != null)
                return rootProp.GetValue(_rootControl);
            return EvaluationSkipped.Instance;
        }

        // Try static member access (Color.Red, AnchorStyles.Top)
        var leftText = memberAccess.Expression.ToString();
        var leftType = ResolveType(leftText);
        if (leftType != null) {
            var field = leftType.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
            if (field != null)
                return field.GetValue(null);

            var prop = leftType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
            if (prop != null)
                return prop.GetValue(null);

            return EvaluationSkipped.Instance;
        }

        // Try resolving left side as an expression
        var leftValue = EvaluateExpression(memberAccess.Expression);
        if (leftValue is EvaluationSkipped)
            return EvaluationSkipped.Instance;
        if (leftValue == null)
            return null;

        var instanceProp = leftValue.GetType().GetProperty(memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
            ?? leftValue.GetType().GetProperty(memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (instanceProp != null)
            return instanceProp.GetValue(leftValue);

        var instanceField = leftValue.GetType().GetField(memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (instanceField != null)
            return instanceField.GetValue(leftValue);

        return EvaluationSkipped.Instance;
    }

    private object? EvaluateInvocationExpression(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess) {
            var methodName = memberAccess.Name.Identifier.Text;

            // Skip resources.GetObject/GetString
            var leftText = memberAccess.Expression.ToString();
            if (leftText.Contains("resources") || leftText.Contains("resource"))
                return EvaluationSkipped.Instance;

            // Static method call: e.g., Color.FromArgb(255, 0, 0)
            var type = ResolveType(leftText);
            if (type != null) {
                var args = EvaluateArgumentList(invocation.ArgumentList);
                if (args.Any(a => a is EvaluationSkipped))
                    return EvaluationSkipped.Instance;

                var argTypes = args.Select(a => a?.GetType() ?? typeof(object)).ToArray();
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, argTypes, null);

                if (method == null) {
                    method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
                }

                if (method != null) {
                    try {
                        var parameters = method.GetParameters();
                        for (int i = 0; i < args.Length; i++) {
                            if (args[i] != null && parameters[i].ParameterType != args[i]!.GetType()) {
                                if (args[i] is IConvertible)
                                    args[i] = Convert.ChangeType(args[i]!, parameters[i].ParameterType);
                            }
                        }
                        return method.Invoke(null, args);
                    }
                    catch {
                        return EvaluationSkipped.Instance;
                    }
                }
            }
        }

        return EvaluationSkipped.Instance;
    }

    private object? EvaluateBitwiseOr(BinaryExpressionSyntax binary) {
        var left = EvaluateExpression(binary.Left);
        var right = EvaluateExpression(binary.Right);

        if (left is EvaluationSkipped || right is EvaluationSkipped)
            return EvaluationSkipped.Instance;

        if (left is Enum leftEnum && right is Enum rightEnum) {
            var leftVal = Convert.ToInt64(leftEnum);
            var rightVal = Convert.ToInt64(rightEnum);
            return Enum.ToObject(leftEnum.GetType(), leftVal | rightVal);
        }

        if (left is int leftInt && right is int rightInt)
            return leftInt | rightInt;

        return EvaluationSkipped.Instance;
    }

    private object? EvaluateArrayCreation(ArrayCreationExpressionSyntax array) {
        if (array.Initializer == null)
            return Array.Empty<object>();

        var items = new List<object?>();
        foreach (var expr in array.Initializer.Expressions) {
            var val = EvaluateExpression(expr);
            if (val is EvaluationSkipped)
                continue;
            items.Add(val);
        }

        var elementTypeName = array.Type.ElementType.ToString();
        var elementType = ResolveType(elementTypeName);

        if (elementType != null && elementType != typeof(object)) {
            var typedArray = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++) {
                if (items[i] != null)
                    typedArray.SetValue(items[i], i);
            }
            return typedArray;
        }

        return items.ToArray();
    }

    private object? EvaluateImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax implicitArray) {
        var items = new List<object?>();
        foreach (var expr in implicitArray.Initializer.Expressions) {
            var val = EvaluateExpression(expr);
            if (val is EvaluationSkipped)
                continue;
            items.Add(val);
        }
        return items.ToArray();
    }

    private object? EvaluateNegation(PrefixUnaryExpressionSyntax prefix) {
        var operand = EvaluateExpression(prefix.Operand);
        if (operand is EvaluationSkipped)
            return EvaluationSkipped.Instance;

        return operand switch {
            int i => -i,
            long l => -l,
            float f => -f,
            double d => -d,
            decimal m => -m,
            _ => EvaluationSkipped.Instance
        };
    }

    private object?[] EvaluateArgumentList(ArgumentListSyntax? argumentList) {
        if (argumentList == null || argumentList.Arguments.Count == 0)
            return Array.Empty<object?>();

        return argumentList.Arguments
            .Select(a => EvaluateExpression(a.Expression))
            .ToArray();
    }

    #endregion

    #region Resolution Helpers

    private string? GetFieldName(ExpressionSyntax left) {
        if (left is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess) {
            return memberAccess.Name.Identifier.Text;
        }

        if (left is IdentifierNameSyntax identifier) {
            return identifier.Identifier.Text;
        }

        return null;
    }

    private object? ResolveTarget(ExpressionSyntax left, out string? propertyName) {
        propertyName = null;

        if (left is MemberAccessExpressionSyntax memberAccess) {
            propertyName = memberAccess.Name.Identifier.Text;

            // this.Property
            if (memberAccess.Expression is ThisExpressionSyntax) {
                return _rootControl;
            }

            // this.button1.Text — nested member access
            if (memberAccess.Expression is MemberAccessExpressionSyntax innerAccess) {
                // this.button1.FlatAppearance.BorderSize — 3-deep
                if (innerAccess.Expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } deepAccess) {
                    var fieldName = deepAccess.Name.Identifier.Text;
                    var nestedPropName = innerAccess.Name.Identifier.Text;
                    if (_components.TryGetValue(fieldName, out var comp)) {
                        var nestedProp = comp.GetType().GetProperty(nestedPropName,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (nestedProp != null)
                            return nestedProp.GetValue(comp);
                    }
                }

                // this.button1.Text — 2-deep
                if (innerAccess.Expression is ThisExpressionSyntax) {
                    var fieldName = innerAccess.Name.Identifier.Text;
                    if (_components.TryGetValue(fieldName, out var comp))
                        return comp;
                }
            }

            // button1.Text (without this.)
            if (memberAccess.Expression is IdentifierNameSyntax identifier) {
                var fieldName = identifier.Identifier.Text;
                if (_components.TryGetValue(fieldName, out var comp))
                    return comp;
            }
        }

        return null;
    }

    private object? ResolveObjectExpression(ExpressionSyntax expression) {
        if (expression is ThisExpressionSyntax)
            return _rootControl;

        if (expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess) {
            if (_components.TryGetValue(memberAccess.Name.Identifier.Text, out var comp))
                return comp;
        }

        if (expression is IdentifierNameSyntax identifier) {
            if (_components.TryGetValue(identifier.Identifier.Text, out var comp))
                return comp;
        }

        return null;
    }

    private object? ResolveIdentifier(string name) {
        if (_components.TryGetValue(name, out var comp))
            return comp;
        return EvaluationSkipped.Instance;
    }

    #endregion

    #region Type Resolution

    internal Type? ResolveType(string typeName) {
        if (_typeCache.TryGetValue(typeName, out var cached))
            return cached;

        var type = ResolveTypeCore(typeName);
        _typeCache[typeName] = type;
        return type;
    }

    private static readonly Dictionary<string, Type> KeywordTypes = new() {
        ["int"] = typeof(int),
        ["uint"] = typeof(uint),
        ["long"] = typeof(long),
        ["ulong"] = typeof(ulong),
        ["short"] = typeof(short),
        ["ushort"] = typeof(ushort),
        ["byte"] = typeof(byte),
        ["sbyte"] = typeof(sbyte),
        ["float"] = typeof(float),
        ["double"] = typeof(double),
        ["decimal"] = typeof(decimal),
        ["bool"] = typeof(bool),
        ["char"] = typeof(char),
        ["string"] = typeof(string),
        ["object"] = typeof(object),
    };

    private Type? ResolveTypeCore(string typeName) {
        if (KeywordTypes.TryGetValue(typeName, out var keywordType))
            return keywordType;

        // Try direct lookup
        var type = Type.GetType(typeName);
        if (type != null)
            return type;

        // Search loaded assemblies (including extras)
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            type = assembly.GetType(typeName);
            if (type != null)
                return type;
        }

        // Also search extra assemblies explicitly (in case not yet in AppDomain)
        foreach (var assembly in _extraAssemblies) {
            type = assembly.GetType(typeName);
            if (type != null)
                return type;
        }

        // Try common namespaces for short names
        var namespaces = new[] {
            "System.Windows.Forms",
            "System.Drawing",
            "System.ComponentModel",
            "System"
        };

        foreach (var ns in namespaces) {
            var fullName = $"{ns}.{typeName}";
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
        }

        return null;
    }

    #endregion

    #region Property Setting

    private void SetPropertyValue(object target, string propertyName, object? value) {
        var prop = target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
            ?? target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null || !prop.CanWrite)
            return;

        try {
            if (value != null && prop.PropertyType != value.GetType()) {
                if (prop.PropertyType == typeof(int) && value is IConvertible)
                    value = Convert.ToInt32(value);
                else if (prop.PropertyType == typeof(float) && value is IConvertible)
                    value = Convert.ToSingle(value);
                else if (prop.PropertyType == typeof(double) && value is IConvertible)
                    value = Convert.ToDouble(value);
            }

            prop.SetValue(target, value);
        }
        catch {
            // Property set failed, skip
        }
    }

    #endregion

    #region Base Type Detection

    /// <summary>
    /// Detect whether the designer code defines a Form or UserControl.
    /// Checks companion file first, then falls back to designer file heuristics.
    /// </summary>
    internal static Type DetectBaseType(string designerContent, string? companionContent) {
        // Check companion file for `: Form` or `: UserControl`
        if (!string.IsNullOrEmpty(companionContent)) {
            if (Regex.IsMatch(companionContent, @":\s*UserControl\b"))
                return typeof(UserControl);
            if (Regex.IsMatch(companionContent, @":\s*Form\b"))
                return typeof(Form);
        }

        // Check designer content for clues
        if (Regex.IsMatch(designerContent, @"this\.AutoScaleMode\s*=\s*System\.Windows\.Forms\.AutoScaleMode")) {
            // Both Form and UserControl can have this, check for FormBorderStyle (Form-only)
            if (Regex.IsMatch(designerContent, @"this\.FormBorderStyle\s*="))
                return typeof(Form);
            // Check for this.ClientSize (commonly Form) vs this.Size (commonly UserControl)
            if (Regex.IsMatch(designerContent, @"this\.ClientSize\s*="))
                return typeof(Form);
        }

        // If the designer has this.Size but not this.ClientSize, likely UserControl
        if (Regex.IsMatch(designerContent, @"this\.Size\s*=") &&
            !Regex.IsMatch(designerContent, @"this\.ClientSize\s*="))
            return typeof(UserControl);

        // Default to Form
        return typeof(Form);
    }

    #endregion

    #region Project Assembly Resolution

    private static List<string> ResolveProjectAssemblyPaths(string projectDir) {
        var paths = new List<string>();
        try {
            var csprojPath = FormRenderingHelpers.FindCsproj(projectDir);
            var csprojDir = Path.GetDirectoryName(csprojPath)!;

            var searchDirs = new[] {
                Path.Combine(csprojDir, "bin", "Debug"),
                Path.Combine(csprojDir, "bin", "Release"),
            };

            foreach (var searchDir in searchDirs) {
                if (!Directory.Exists(searchDir))
                    continue;

                var tfmDirs = Directory.GetDirectories(searchDir)
                    .OrderByDescending(Directory.GetLastWriteTime)
                    .ToArray();

                foreach (var tfmDir in tfmDirs) {
                    var dlls = Directory.GetFiles(tfmDir, "*.dll");
                    foreach (var dll in dlls)
                        paths.Add(dll);
                    if (paths.Count > 0)
                        return paths;
                }
            }
        }
        catch { /* no csproj or no build output */ }

        return paths;
    }

    #endregion

    #region Recursive UserControl Rendering

    /// <summary>
    /// Attempt to find a .Designer.cs file in the project directory whose class name
    /// matches the unresolved type, render it recursively, and return a PictureBox
    /// containing the rendered bitmap. Returns null if not possible.
    /// </summary>
    private Panel? TryRenderUserControlFromSource(string typeName) {
        if (_projectDir == null)
            return null;

        // Extract the short class name from the fully qualified type name
        var shortName = typeName.Contains('.')
            ? typeName.Substring(typeName.LastIndexOf('.') + 1)
            : typeName;

        // Circular reference protection
        lock (RenderingStackLock) {
            if (!RenderingStack.Add(shortName))
                return null; // Already rendering this type — circular reference
        }

        try {
            var designerFile = FindDesignerFileForType(shortName, _projectDir);
            if (designerFile == null)
                return null;

            var designerContent = File.ReadAllText(designerFile);

            // Look for companion .cs file
            var idx = designerFile.LastIndexOf(".Designer.cs", StringComparison.OrdinalIgnoreCase);
            var companionPath = idx >= 0 ? designerFile.Substring(0, idx) + ".cs" : null;
            string? companionContent = null;
            if (companionPath != null && File.Exists(companionPath))
                companionContent = File.ReadAllText(companionPath);

            // Render with a child renderer, passing project dir for further recursion
            var childRenderer = new DesignSurfaceFormRenderer();
            var extraPaths = ResolveProjectAssemblyPaths(_projectDir);
            var pngBytes = childRenderer.RenderDesignerCode(designerContent, companionContent, extraPaths, _projectDir);

            // Create a Panel with the rendered image as background
            using var ms = new MemoryStream(pngBytes);
            var bitmap = new Bitmap(ms);

            var panel = new Panel {
                Size = bitmap.Size,
                BackgroundImage = bitmap,
                BackgroundImageLayout = ImageLayout.None,
                BorderStyle = BorderStyle.FixedSingle
            };
            return panel;
        }
        catch {
            // Recursive rendering failed — caller will fall back to error placeholder
            return null;
        }
        finally {
            lock (RenderingStackLock) {
                RenderingStack.Remove(shortName);
            }
        }
    }

    /// <summary>
    /// Search the project directory tree for a .Designer.cs file whose class name matches.
    /// </summary>
    internal static string? FindDesignerFileForType(string shortClassName, string projectDir) {
        try {
            // First try the obvious file name: ClassName.Designer.cs
            var candidateFiles = Directory.GetFiles(projectDir, $"{shortClassName}.Designer.cs", SearchOption.AllDirectories);
            if (candidateFiles.Length > 0)
                return candidateFiles[0];

            // Broader search: scan all .Designer.cs files for a matching class declaration
            var allDesignerFiles = Directory.GetFiles(projectDir, "*.Designer.cs", SearchOption.AllDirectories);
            foreach (var file in allDesignerFiles) {
                // Skip bin/obj directories
                if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                    continue;

                var content = File.ReadAllText(file);
                if (Regex.IsMatch(content, $@"\bclass\s+{Regex.Escape(shortClassName)}\b"))
                    return file;
            }
        }
        catch { /* directory not searchable */ }

        return null;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Creates a red-bordered error placeholder panel matching VS designer behavior
    /// when a control fails to load or throws during initialization.
    /// </summary>
    internal static Panel CreateErrorPlaceholder(string typeName, string errorMessage) {
        var panel = new Panel {
            BackColor = Color.FromArgb(255, 240, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(200, 50)
        };
        var label = new Label {
            Text = $"{typeName}\n{errorMessage}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DarkRed,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
            AutoEllipsis = true
        };
        panel.Controls.Add(label);
        return panel;
    }

    private static string ComputeHash(string content) {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    #endregion
}