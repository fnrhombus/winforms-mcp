using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Renders WinForms .Designer.cs files to PNG images using Roslyn parsing and reflection.
/// Parses the InitializeComponent() method via Roslyn's syntax tree, then executes
/// statements via reflection to create real WinForms controls and render them with DrawToBitmap.
/// </summary>
public class SyntaxTreeFormRenderer {
    private static bool _visualStylesInitialized;
    private readonly Dictionary<string, Type?> _typeCache = new();
    private Dictionary<string, object> _fields = new();
    private Form? _form;

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
    /// Render a .Designer.cs file to a PNG image.
    /// </summary>
    /// <param name="designerCode">The C# source code of the .Designer.cs file.</param>
    /// <param name="outputPath">Path to save the rendered PNG.</param>
    public void RenderDesignerCode(string designerCode, string outputPath) {
        var tree = CSharpSyntaxTree.ParseText(designerCode);
        var root = tree.GetCompilationUnitRoot();

        var initMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "InitializeComponent");

        if (initMethod?.Body == null)
            throw new InvalidOperationException("InitializeComponent() method not found in designer code.");

        // Enable visual styles for consistent rendering with compiled version
        EnsureVisualStyles();

        _fields = new Dictionary<string, object>();
        _form = new Form();

        foreach (var statement in initMethod.Body.Statements) {
            try {
                ExecuteStatement(statement);
            }
            catch {
                // Graceful degradation: skip statements that can't be executed
            }
        }

        RenderFormToBitmap(outputPath);
    }

    /// <summary>
    /// Render a .Designer.cs file to a PNG image.
    /// </summary>
    /// <param name="designerFilePath">Path to the .Designer.cs file.</param>
    /// <param name="outputPath">Path to save the rendered PNG.</param>
    public void RenderDesignerFile(string designerFilePath, string outputPath) {
        var designerCode = File.ReadAllText(designerFilePath);
        RenderDesignerCode(designerCode, outputPath);
    }

    /// <summary>
    /// Render designer code to PNG bytes without writing to disk.
    /// </summary>
    /// <param name="designerCode">The C# source code of the .Designer.cs file.</param>
    /// <returns>PNG image as a byte array.</returns>
    public byte[] RenderDesignerCodeToBytes(string designerCode) {
        var tree = CSharpSyntaxTree.ParseText(designerCode);
        var root = tree.GetCompilationUnitRoot();

        var initMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "InitializeComponent");

        if (initMethod?.Body == null)
            throw new InvalidOperationException("InitializeComponent() method not found in designer code.");

        EnsureVisualStyles();

        _fields = new Dictionary<string, object>();
        _form = new Form();

        foreach (var statement in initMethod.Body.Statements) {
            try {
                ExecuteStatement(statement);
            }
            catch {
                // Graceful degradation: skip statements that can't be executed
            }
        }

        return RenderFormToBytes();
    }

    /// <summary>
    /// Render a .Designer.cs file to PNG bytes without writing to disk.
    /// </summary>
    /// <param name="designerFilePath">Path to the .Designer.cs file.</param>
    /// <returns>PNG image as a byte array.</returns>
    public byte[] RenderDesignerFileToBytes(string designerFilePath) {
        var designerCode = File.ReadAllText(designerFilePath);
        return RenderDesignerCodeToBytes(designerCode);
    }

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

        // this.button1 = new System.Windows.Forms.Button();
        // or: button1 = new Button();
        if (right is ObjectCreationExpressionSyntax creation) {
            var fieldName = GetFieldName(left);

            if (fieldName != null) {
                var typeName = creation.Type.ToString();
                var type = ResolveType(typeName);
                if (type == null)
                    return;

                // Special case: skip IContainer (components)
                if (typeof(System.ComponentModel.IContainer).IsAssignableFrom(type)) {
                    var instance = Activator.CreateInstance(type);
                    if (instance != null)
                        _fields[fieldName] = instance;
                    return;
                }

                try {
                    var args = EvaluateArgumentList(creation.ArgumentList);
                    var instance = args.Length > 0
                        ? Activator.CreateInstance(type, args)
                        : Activator.CreateInstance(type);
                    if (instance != null) {
                        // Check if this is a property on the form (e.g., this.ClientSize, this.Font)
                        // rather than a field declaration (e.g., this.button1 = new Button())
                        var formProp = _form?.GetType().GetProperty(fieldName,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (formProp != null && formProp.CanWrite) {
                            SetPropertyValue(_form!, fieldName, instance);
                        }
                        else {
                            _fields[fieldName] = instance;
                        }
                    }
                }
                catch {
                    // Type couldn't be instantiated, skip
                }
                return;
            }
            // fieldName is null: nested property like this.control.Prop = new Type(...)
            // Fall through to property-setting logic below
        }

        // this.button1.Text = "Click";
        // this.ClientSize = new Size(400, 300);
        var target = ResolveTarget(left, out var propertyName);
        if (target == null || propertyName == null)
            return;

        var value = EvaluateExpression(right);
        if (value is EvaluationSkipped)
            return;

        SetPropertyValue(target, propertyName, value);
    }

    private void SetPropertyValue(object target, string propertyName, object? value) {
        // Try DeclaredOnly first to avoid AmbiguousMatchException on shadowed properties
        // (e.g., CheckedListBox.Items shadows ListBox.Items with 'new' keyword)
        var prop = target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
            ?? target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null || !prop.CanWrite)
            return;

        try {
            // Handle numeric type conversions
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

    private void ExecuteInvocation(InvocationExpressionSyntax invocation) {
        var expr = invocation.Expression;

        if (expr is MemberAccessExpressionSyntax memberAccess) {
            var methodName = memberAccess.Name.Identifier.Text;

            // Handle Controls.Add(this.button1) / Controls.Add(button1)
            if (methodName == "Add" && memberAccess.Expression is MemberAccessExpressionSyntax parentAccess
                && parentAccess.Name.Identifier.Text == "Controls") {
                var parentObj = ResolveObjectExpression(parentAccess.Expression);
                if (parentObj is Control parentControl && invocation.ArgumentList.Arguments.Count == 1) {
                    var childExpr = invocation.ArgumentList.Arguments[0].Expression;
                    var childObj = EvaluateExpression(childExpr);
                    if (childObj is Control childControl) {
                        parentControl.Controls.Add(childControl);
                    }
                }
                return;
            }

            // Handle Items.AddRange(...)
            if (methodName == "AddRange" && memberAccess.Expression is MemberAccessExpressionSyntax itemsAccess
                && itemsAccess.Name.Identifier.Text == "Items") {
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

            // Handle SuspendLayout(), ResumeLayout(), PerformLayout()
            if (methodName is "SuspendLayout" or "ResumeLayout" or "PerformLayout") {
                var targetObj = ResolveObjectExpression(memberAccess.Expression);
                if (targetObj != null) {
                    var method = targetObj.GetType().GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (method != null) {
                        method.Invoke(targetObj, null);
                        return;
                    }

                    // Try with bool parameter for ResumeLayout(false)
                    if (methodName == "ResumeLayout" && invocation.ArgumentList.Arguments.Count == 1) {
                        var boolMethod = targetObj.GetType().GetMethod(methodName,
                            BindingFlags.Public | BindingFlags.Instance,
                            null, new[] { typeof(bool) }, null);
                        if (boolMethod != null) {
                            var arg = EvaluateExpression(invocation.ArgumentList.Arguments[0].Expression);
                            if (arg is bool boolArg) {
                                boolMethod.Invoke(targetObj, new object[] { boolArg });
                            }
                        }
                    }
                }
                return;
            }

            // Handle general method calls on objects (e.g., this.Controls.AddRange)
            if (methodName == "AddRange" && memberAccess.Expression is MemberAccessExpressionSyntax controlsAccess
                && controlsAccess.Name.Identifier.Text == "Controls") {
                var parentObj = ResolveObjectExpression(controlsAccess.Expression);
                if (parentObj is Control parentCtrl && invocation.ArgumentList.Arguments.Count == 1) {
                    var argValue = EvaluateExpression(invocation.ArgumentList.Arguments[0].Expression);
                    if (argValue is Control[] controls) {
                        parentCtrl.Controls.AddRange(controls);
                    }
                    else if (argValue is object[] objs) {
                        var controlList = objs.OfType<Control>().ToArray();
                        if (controlList.Length > 0)
                            parentCtrl.Controls.AddRange(controlList);
                    }
                }
                return;
            }
        }
    }

    internal object? EvaluateExpression(ExpressionSyntax expression) {
        switch (expression) {
            case LiteralExpressionSyntax literal:
                return EvaluateLiteral(literal);

            case ObjectCreationExpressionSyntax creation:
                return EvaluateObjectCreation(creation);

            case MemberAccessExpressionSyntax memberAccess:
                return EvaluateMemberAccess(memberAccess);

            case InvocationExpressionSyntax invocation:
                return EvaluateInvocationExpression(invocation);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.BitwiseOrExpression):
                return EvaluateBitwiseOr(binary);

            case CastExpressionSyntax cast:
                return EvaluateExpression(cast.Expression);

            case ParenthesizedExpressionSyntax paren:
                return EvaluateExpression(paren.Expression);

            case ArrayCreationExpressionSyntax array:
                return EvaluateArrayCreation(array);

            case ImplicitArrayCreationExpressionSyntax implicitArray:
                return EvaluateImplicitArrayCreation(implicitArray);

            case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.UnaryMinusExpression):
                return EvaluateNegation(prefix);

            case IdentifierNameSyntax identifier:
                return ResolveIdentifier(identifier.Identifier.Text);

            case ThisExpressionSyntax:
                return _form;

            default:
                return EvaluationSkipped.Instance;
        }
    }

    private object? EvaluateLiteral(LiteralExpressionSyntax literal) {
        return literal.Token.Value;
    }

    private object? EvaluateObjectCreation(ObjectCreationExpressionSyntax creation) {
        var typeName = creation.Type.ToString();
        var type = ResolveType(typeName);
        if (type == null)
            return EvaluationSkipped.Instance;

        var args = EvaluateArgumentList(creation.ArgumentList);
        // Check for EvaluationSkipped in args
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

        // Handle this.field references (e.g., this.button1)
        if (memberAccess.Expression is ThisExpressionSyntax) {
            if (_fields.TryGetValue(memberName, out var fieldValue))
                return fieldValue;
            // It might be a property on the form
            var formProp = _form?.GetType().GetProperty(memberName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (formProp != null)
                return formProp.GetValue(_form);
            return EvaluationSkipped.Instance;
        }

        // Handle chained member access: e.g., Color.Red, AnchorStyles.Top
        // First try to resolve the left side as a type for static member access
        var leftText = memberAccess.Expression.ToString();
        var leftType = ResolveType(leftText);
        if (leftType != null) {
            // Static field
            var field = leftType.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
            if (field != null)
                return field.GetValue(null);

            // Static property
            var prop = leftType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
            if (prop != null)
                return prop.GetValue(null);

            return EvaluationSkipped.Instance;
        }

        // Try resolving left side as an expression (e.g., instance.Property)
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

            // Handle resources.GetObject(...) / resources.GetString(...) → skip
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

                // If exact match fails, try finding by name and arg count
                if (method == null) {
                    method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
                }

                if (method != null) {
                    try {
                        // Convert args to match parameter types
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
            var underlyingType = Enum.GetUnderlyingType(leftEnum.GetType());
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

        // Try to determine element type from the array type
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

    private string? GetFieldName(ExpressionSyntax left) {
        // this.button1
        if (left is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression is ThisExpressionSyntax) {
            return memberAccess.Name.Identifier.Text;
        }

        // button1 (without this.)
        if (left is IdentifierNameSyntax identifier) {
            return identifier.Identifier.Text;
        }

        return null;
    }

    private object? ResolveTarget(ExpressionSyntax left, out string? propertyName) {
        propertyName = null;

        // this.button1.Text or this.Text
        if (left is MemberAccessExpressionSyntax memberAccess) {
            propertyName = memberAccess.Name.Identifier.Text;

            // this.Property (form property)
            if (memberAccess.Expression is ThisExpressionSyntax) {
                return _form;
            }

            // this.button1.Text
            if (memberAccess.Expression is MemberAccessExpressionSyntax innerAccess
                && innerAccess.Expression is ThisExpressionSyntax) {
                var fieldName = innerAccess.Name.Identifier.Text;
                if (_fields.TryGetValue(fieldName, out var obj))
                    return obj;
            }

            // button1.Text (without this.)
            if (memberAccess.Expression is IdentifierNameSyntax identifier) {
                var fieldName = identifier.Identifier.Text;
                if (_fields.TryGetValue(fieldName, out var obj))
                    return obj;
            }
        }

        return null;
    }

    private object? ResolveObjectExpression(ExpressionSyntax expression) {
        if (expression is ThisExpressionSyntax)
            return _form;

        if (expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression is ThisExpressionSyntax) {
            var fieldName = memberAccess.Name.Identifier.Text;
            if (_fields.TryGetValue(fieldName, out var obj))
                return obj;
        }

        if (expression is IdentifierNameSyntax identifier) {
            if (_fields.TryGetValue(identifier.Identifier.Text, out var obj))
                return obj;
        }

        return null;
    }

    private object? ResolveIdentifier(string name) {
        if (_fields.TryGetValue(name, out var obj))
            return obj;
        return EvaluationSkipped.Instance;
    }

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

    private static Type? ResolveTypeCore(string typeName) {
        // C# keyword types (int, string, bool, etc.)
        if (KeywordTypes.TryGetValue(typeName, out var keywordType))
            return keywordType;

        // Try direct lookup first (fully-qualified names)
        var type = Type.GetType(typeName);
        if (type != null)
            return type;

        // Search loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            type = assembly.GetType(typeName);
            if (type != null)
                return type;
        }

        // Try common WinForms/Drawing namespaces for short names
        var namespaces = new[]
        {
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

    private byte[] RenderFormToBytes() {
        if (_form == null)
            throw new InvalidOperationException("Failed to render form: form was not created.");

        Bitmap? bitmap = null;
        Exception? threadException = null;

        var thread = new Thread(() => {
            try {
                _form.ShowInTaskbar = false;
                _form.StartPosition = FormStartPosition.Manual;
                _form.Location = new Point(-32000, -32000);
                _form.Show();

                var width = _form.Width > 0 ? _form.Width : 300;
                var height = _form.Height > 0 ? _form.Height : 200;
                bitmap = new Bitmap(width, height);
                _form.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));

                _form.Close();
            }
            catch (Exception ex) {
                threadException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(timeout: TimeSpan.FromMilliseconds(5000));

        if (threadException != null)
            throw new InvalidOperationException($"Failed to render form: {threadException.Message}", threadException);

        if (bitmap == null)
            throw new InvalidOperationException("Failed to render form: bitmap was not created.");

        byte[] pngBytes;
        using (var ms = new System.IO.MemoryStream()) {
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngBytes = ms.ToArray();
        }
        bitmap.Dispose();
        _form.Dispose();

        return pngBytes;
    }

    private void RenderFormToBitmap(string outputPath) {
        // Ensure output directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var pngBytes = RenderFormToBytes();
        File.WriteAllBytes(outputPath, pngBytes);
    }
}

/// <summary>
/// Sentinel type indicating an expression could not be evaluated and should be skipped.
/// </summary>
internal sealed class EvaluationSkipped {
    public static readonly EvaluationSkipped Instance = new();
    private EvaluationSkipped() { }
}