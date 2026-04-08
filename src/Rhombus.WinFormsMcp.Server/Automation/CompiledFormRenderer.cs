using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Renders WinForms forms by compiling a temporary project that references
/// the same packages as the source project, then running it to capture a PNG.
/// Handles custom controls, third-party references, and modern designer syntax.
/// </summary>
public class CompiledFormRenderer
{
    private readonly Dictionary<string, byte[]> _cache = new();

    /// <summary>
    /// Render a WinForms form to a PNG image by compiling a temp project.
    /// </summary>
    /// <param name="sourceFilePath">Path to .cs or .Designer.cs file.</param>
    /// <returns>PNG image bytes.</returns>
    public byte[] RenderForm(string sourceFilePath)
    {
        var designerFile = ResolveDesignerFile(sourceFilePath);
        var designerContent = File.ReadAllText(designerFile);
        var csprojPath = FindCsproj(Path.GetDirectoryName(designerFile)!);
        var csprojContent = File.ReadAllText(csprojPath);

        var cacheKey = ComputeHash(designerContent + csprojContent);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var tempDir = Path.Combine(Path.GetTempPath(), $"WinFormsMcp_render_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var (ns, className, eventHandlers) = ParseDesignerFile(designerContent);

            GenerateCsproj(tempDir, csprojPath);
            File.Copy(designerFile, Path.Combine(tempDir, Path.GetFileName(designerFile)));

            // Copy .resx if it exists (needed for forms with embedded resources)
            var resxPath = Path.ChangeExtension(designerFile.Replace(".Designer.cs", ".resx", StringComparison.OrdinalIgnoreCase), null);
            resxPath = Path.Combine(Path.GetDirectoryName(designerFile)!,
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(designerFile)) + ".resx");
            if (File.Exists(resxPath))
                File.Copy(resxPath, Path.Combine(tempDir, Path.GetFileName(resxPath)));

            GenerateCodeBehind(tempDir, ns, className, eventHandlers);
            GenerateProgram(tempDir, ns, className);

            var pngBytes = BuildAndCapture(tempDir);
            _cache[cacheKey] = pngBytes;
            return pngBytes;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Resolve the .Designer.cs file from a source file path.
    /// If given a .cs file, looks for the sibling .Designer.cs.
    /// Throws if no separate designer file exists.
    /// </summary>
    internal static string ResolveDesignerFile(string sourceFilePath)
    {
        if (sourceFilePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Designer file not found: {sourceFilePath}");
            return sourceFilePath;
        }

        var dir = Path.GetDirectoryName(sourceFilePath)
            ?? throw new ArgumentException($"Cannot determine directory for: {sourceFilePath}");
        var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var designerFile = Path.Combine(dir, $"{baseName}.Designer.cs");

        if (!File.Exists(designerFile))
            throw new FileNotFoundException(
                $"No separate Designer.cs file found for {sourceFilePath}. " +
                $"This tool requires a standard WinForms designer file. Expected: {designerFile}");

        return designerFile;
    }

    /// <summary>
    /// Walk up from a directory to find the nearest .csproj file.
    /// </summary>
    internal static string FindCsproj(string directory)
    {
        var dir = directory;
        while (dir != null)
        {
            var csprojFiles = Directory.GetFiles(dir, "*.csproj");
            if (csprojFiles.Length > 0)
                return csprojFiles[0];
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"No .csproj file found in directory tree above {directory}");
    }

    /// <summary>
    /// Parse the designer file to extract namespace, class name, and event handler names.
    /// </summary>
    internal static (string? Namespace, string ClassName, List<string> EventHandlers) ParseDesignerFile(string content)
    {
        var nsMatch = Regex.Match(content, @"namespace\s+([\w.]+)");
        var ns = nsMatch.Success ? nsMatch.Groups[1].Value : null;

        var classMatch = Regex.Match(content, @"partial\s+class\s+(\w+)");
        if (!classMatch.Success)
            throw new InvalidOperationException("Could not find partial class declaration in designer file.");
        var className = classMatch.Groups[1].Value;

        var eventHandlers = new List<string>();
        // Match both: += this.Handler; and += new EventHandler(this.Handler);
        var eventMatches = Regex.Matches(content, @"\+=\s*(?:new\s+[\w.]+\s*\(\s*)?(?:this\.)?(\w+)\s*\)?\s*;");
        foreach (Match m in eventMatches)
        {
            var handlerName = m.Groups[1].Value;
            if (!eventHandlers.Contains(handlerName))
                eventHandlers.Add(handlerName);
        }

        return (ns, className, eventHandlers);
    }

    /// <summary>
    /// Generate a minimal .csproj that mirrors the source project's references.
    /// </summary>
    internal static void GenerateCsproj(string tempDir, string sourceCsprojPath)
    {
        var sourceDoc = XDocument.Load(sourceCsprojPath);
        var sourceRoot = sourceDoc.Root!;
        XNamespace msbuild = sourceRoot.GetDefaultNamespace();

        var tfm = sourceRoot.Descendants(msbuild + "TargetFramework").FirstOrDefault()?.Value ?? "net8.0-windows";

        var csproj = new XDocument(
            new XElement("Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("OutputType", "Exe"),
                    new XElement("TargetFramework", tfm),
                    new XElement("UseWindowsForms", "true"),
                    new XElement("ImplicitUsings", "enable"),
                    new XElement("Nullable", "enable")
                )
            )
        );

        // Copy PackageReferences
        var packageRefs = sourceRoot.Descendants(msbuild + "PackageReference").ToList();
        if (packageRefs.Count > 0)
        {
            var itemGroup = new XElement("ItemGroup");
            foreach (var pr in packageRefs)
            {
                var elem = new XElement("PackageReference");
                foreach (var attr in pr.Attributes())
                    elem.Add(new XAttribute(attr.Name, attr.Value));
                itemGroup.Add(elem);
            }
            csproj.Root!.Add(itemGroup);
        }

        // Reference the source project's built assembly for custom controls
        var projectDll = FindProjectOutputDll(sourceCsprojPath);
        if (projectDll != null)
        {
            var refGroup = new XElement("ItemGroup",
                new XElement("Reference",
                    new XAttribute("Include", Path.GetFileNameWithoutExtension(projectDll)),
                    new XElement("HintPath", projectDll)));
            csproj.Root!.Add(refGroup);
        }

        csproj.Save(Path.Combine(tempDir, "TempFormRender.csproj"));
    }

    /// <summary>
    /// Find the built DLL for a project by scanning its bin directory.
    /// </summary>
    internal static string? FindProjectOutputDll(string csprojPath)
    {
        var csprojDir = Path.GetDirectoryName(csprojPath)!;
        var projectName = Path.GetFileNameWithoutExtension(csprojPath);

        var searchDirs = new[]
        {
            Path.Combine(csprojDir, "bin", "Debug"),
            Path.Combine(csprojDir, "bin", "Release"),
        };

        foreach (var searchDir in searchDirs)
        {
            if (!Directory.Exists(searchDir)) continue;

            var tfmDirs = Directory.GetDirectories(searchDir)
                .OrderByDescending(Directory.GetLastWriteTime);

            foreach (var tfmDir in tfmDirs)
            {
                var dll = Path.Combine(tfmDir, $"{projectName}.dll");
                if (File.Exists(dll))
                    return dll;
            }
        }

        return null;
    }

    /// <summary>
    /// Generate the code-behind partial class that inherits Form and stubs event handlers.
    /// </summary>
    internal static void GenerateCodeBehind(string tempDir, string? ns, string className, List<string> eventHandlers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Windows.Forms;");
        sb.AppendLine();

        if (ns != null)
        {
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
        }

        sb.AppendLine($"partial class {className} : Form");
        sb.AppendLine("{");
        sb.AppendLine($"    public {className}()");
        sb.AppendLine("    {");
        sb.AppendLine("        InitializeComponent();");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var handler in eventHandlers)
        {
            sb.AppendLine($"    private void {handler}(object? sender, EventArgs e) {{ }}");
        }

        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(tempDir, $"{className}.cs"), sb.ToString());
    }

    /// <summary>
    /// Generate Program.cs that instantiates the form, renders to bitmap, and outputs base64 PNG to stdout.
    /// </summary>
    internal static void GenerateProgram(string tempDir, string? ns, string className)
    {
        var fullClassName = ns != null ? $"{ns}.{className}" : className;

        var program = $$"""
            using System;
            using System.Drawing;
            using System.Drawing.Imaging;
            using System.IO;
            using System.Windows.Forms;

            class Program
            {
                [STAThread]
                static void Main()
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    var form = new {{fullClassName}}();
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.Show();

                    var w = form.Width > 0 ? form.Width : 300;
                    var h = form.Height > 0 ? form.Height : 200;
                    using var bmp = new Bitmap(w, h);
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    form.Close();
                    form.Dispose();

                    using var ms = new MemoryStream();
                    bmp.Save(ms, ImageFormat.Png);
                    Console.Write(Convert.ToBase64String(ms.ToArray()));
                }
            }
            """;

        File.WriteAllText(Path.Combine(tempDir, "Program.cs"), program);
    }

    private static byte[] BuildAndCapture(string tempDir)
    {
        var csprojPath = Path.Combine(tempDir, "TempFormRender.csproj");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{csprojPath}\" -c Release",
            WorkingDirectory = tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Compilation/run failed (exit code {process.ExitCode}):\n{stderr}");

        if (string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(
                $"No output from render process. Stderr:\n{stderr}");

        return Convert.FromBase64String(stdout.Trim());
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
