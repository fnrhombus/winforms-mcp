using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Renders WinForms forms by compiling designer code in-memory with Roslyn
/// and loading the resulting assembly in-process. No temp files or dotnet build.
/// </summary>
public class InProcessFormRenderer {
    private static bool _visualStylesInitialized;
    private readonly Dictionary<string, byte[]> _cache = new();

    private static void EnsureVisualStyles() {
        if (_visualStylesInitialized)
            return;
        try {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
        catch { /* already initialized */ }
        _visualStylesInitialized = true;
    }

    public byte[] RenderForm(string sourceFilePath) {
        var designerFile = CompiledFormRenderer.ResolveDesignerFile(sourceFilePath);
        var designerContent = File.ReadAllText(designerFile);

        var cacheKey = ComputeHash(designerContent);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        EnsureVisualStyles();

        // Resolve third-party assembly references from the source project
        var projectDir = Path.GetDirectoryName(designerFile)!;
        var extraRefs = ResolveProjectAssemblies(projectDir);

        var (ns, className, eventHandlers) = CompiledFormRenderer.ParseDesignerFile(designerContent);
        var codeBehind = GenerateCodeBehind(ns, className, eventHandlers);

        var assembly = CompileInMemory(designerContent, codeBehind, extraRefs);
        var pngBytes = RenderFromAssembly(assembly, ns, className);

        _cache[cacheKey] = pngBytes;
        return pngBytes;
    }

    public byte[] RenderDesignerCode(string designerContent, IEnumerable<string>? extraAssemblyPaths = null) {
        var cacheKey = ComputeHash(designerContent);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        EnsureVisualStyles();

        var extraRefs = extraAssemblyPaths?
            .Where(File.Exists)
            .Select(p => MetadataReference.CreateFromFile(p) as MetadataReference)
            .ToList() ?? new List<MetadataReference>();

        var (ns, className, eventHandlers) = CompiledFormRenderer.ParseDesignerFile(designerContent);
        var codeBehind = GenerateCodeBehind(ns, className, eventHandlers);

        var assembly = CompileInMemory(designerContent, codeBehind, extraRefs);
        var pngBytes = RenderFromAssembly(assembly, ns, className);

        _cache[cacheKey] = pngBytes;
        return pngBytes;
    }

    private static string GenerateCodeBehind(string? ns, string className, List<string> eventHandlers) {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Windows.Forms;");
        sb.AppendLine();

        if (ns != null) {
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
        }

        sb.AppendLine($"public partial class {className} : Form");
        sb.AppendLine("{");
        sb.AppendLine($"    public {className}()");
        sb.AppendLine("    {");
        sb.AppendLine("        InitializeComponent();");
        sb.AppendLine("    }");

        foreach (var handler in eventHandlers) {
            sb.AppendLine($"    private void {handler}(object sender, EventArgs e) {{ }}");
        }

        sb.AppendLine("}");

        if (ns != null)
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static Assembly CompileInMemory(string designerCode, string codeBehind,
        List<MetadataReference>? extraReferences = null) {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(designerCode),
            CSharpSyntaxTree.ParseText(codeBehind),
        };

        var references = GetMetadataReferences();
        if (extraReferences != null)
            references.AddRange(extraReferences);

        var compilation = CSharpCompilation.Create(
            $"FormRender_{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success) {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            throw new InvalidOperationException(
                $"Compilation failed:\n{string.Join("\n", errors)}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(ms.ToArray());
    }

    /// <summary>
    /// Resolve assemblies from the source project's build output.
    /// Adds them as MetadataReferences for compilation AND loads them into the
    /// runtime so custom controls can be instantiated by the compiled form.
    /// </summary>
    private static List<MetadataReference> ResolveProjectAssemblies(string projectDir) {
        var refs = new List<MetadataReference>();
        try {
            var csprojPath = CompiledFormRenderer.FindCsproj(projectDir);
            var csprojDir = Path.GetDirectoryName(csprojPath)!;

            var searchDirs = new[]
            {
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
                    foreach (var dll in dlls) {
                        try {
                            refs.Add(MetadataReference.CreateFromFile(dll));
                            // Also load into runtime so types are available for instantiation
                            Assembly.LoadFrom(dll);
                        }
                        catch { /* skip unreadable DLLs */ }
                    }
                    if (refs.Count > 0)
                        return refs;
                }
            }
        }
        catch { /* no csproj or no build output — proceed without extras */ }

        return refs;
    }

    private static List<MetadataReference> GetMetadataReferences() {
        var refs = new List<MetadataReference>();

        // Add all currently loaded assemblies that have a location
        // This captures the WinForms, System.Drawing, and runtime assemblies
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location)) {
                try {
                    refs.Add(MetadataReference.CreateFromFile(asm.Location));
                }
                catch { /* skip inaccessible assemblies */ }
            }
        }

        // Ensure critical WinForms assemblies are included
        EnsureReference(refs, typeof(Form));           // System.Windows.Forms
        EnsureReference(refs, typeof(Bitmap));          // System.Drawing
        EnsureReference(refs, typeof(Color));           // System.Drawing.Primitives
        EnsureReference(refs, typeof(ImageFormat));     // System.Drawing.Common
        EnsureReference(refs, typeof(System.ComponentModel.IContainer)); // System.ComponentModel
        EnsureReference(refs, typeof(System.ComponentModel.Component));  // System.ComponentModel

        return refs;
    }

    private static void EnsureReference(List<MetadataReference> refs, Type type) {
        var location = type.Assembly.Location;
        if (!string.IsNullOrEmpty(location) && !refs.Any(r =>
            r is PortableExecutableReference pe && pe.FilePath == location)) {
            refs.Add(MetadataReference.CreateFromFile(location));
        }
    }

    private static byte[] RenderFromAssembly(Assembly assembly, string? ns, string className) {
        var fullName = ns != null ? $"{ns}.{className}" : className;
        var formType = assembly.GetType(fullName)
            ?? throw new InvalidOperationException($"Type '{fullName}' not found in compiled assembly.");

        using var form = (Form)Activator.CreateInstance(formType)!;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();

        var w = form.Width > 0 ? form.Width : 300;
        var h = form.Height > 0 ? form.Height : 200;
        using var bmp = new Bitmap(w, h);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
        form.Close();

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static string ComputeHash(string content) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}