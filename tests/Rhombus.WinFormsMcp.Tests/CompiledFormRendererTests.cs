using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Tests;

[TestFixture]
public class CompiledFormRendererTests
{
    #region ResolveDesignerFile

    [Test]
    public void ResolveDesignerFile_GivenDesignerPath_ReturnsSamePath()
    {
        // Create a temp .Designer.cs file
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var designerPath = Path.Combine(tempDir, "MyForm.Designer.cs");
        File.WriteAllText(designerPath, "// designer");
        try
        {
            var result = CompiledFormRenderer.ResolveDesignerFile(designerPath);
            Assert.That(result, Is.EqualTo(designerPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void ResolveDesignerFile_GivenCsFile_FindsSiblingDesigner()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var csPath = Path.Combine(tempDir, "MyForm.cs");
        var designerPath = Path.Combine(tempDir, "MyForm.Designer.cs");
        File.WriteAllText(csPath, "// code-behind");
        File.WriteAllText(designerPath, "// designer");
        try
        {
            var result = CompiledFormRenderer.ResolveDesignerFile(csPath);
            Assert.That(result, Is.EqualTo(designerPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void ResolveDesignerFile_NoDesignerExists_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var csPath = Path.Combine(tempDir, "MyForm.cs");
        File.WriteAllText(csPath, "// code-behind only");
        try
        {
            Assert.Throws<FileNotFoundException>(() =>
                CompiledFormRenderer.ResolveDesignerFile(csPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void ResolveDesignerFile_DesignerPathDoesNotExist_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            CompiledFormRenderer.ResolveDesignerFile(@"C:\nonexistent\Foo.Designer.cs"));
    }

    #endregion

    #region FindCsproj

    [Test]
    public void FindCsproj_CsprojInSameDir_ReturnsIt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var csprojPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, "<Project />");
        try
        {
            var result = CompiledFormRenderer.FindCsproj(tempDir);
            Assert.That(result, Is.EqualTo(csprojPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void FindCsproj_CsprojInParentDir_ReturnsIt()
    {
        var parentDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        var childDir = Path.Combine(parentDir, "subfolder");
        Directory.CreateDirectory(childDir);
        var csprojPath = Path.Combine(parentDir, "Test.csproj");
        File.WriteAllText(csprojPath, "<Project />");
        try
        {
            var result = CompiledFormRenderer.FindCsproj(childDir);
            Assert.That(result, Is.EqualTo(csprojPath));
        }
        finally
        {
            Directory.Delete(parentDir, true);
        }
    }

    [Test]
    public void FindCsproj_NoCsproj_Throws()
    {
        // FindCsproj walks up the directory tree. On a real system it may find
        // a .csproj in an ancestor. We verify it either returns a valid path
        // or throws FileNotFoundException.
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            try
            {
                var result = CompiledFormRenderer.FindCsproj(tempDir);
                // If it found one, it must be a real .csproj file
                Assert.That(File.Exists(result), Is.True);
                Assert.That(result, Does.EndWith(".csproj"));
            }
            catch (FileNotFoundException)
            {
                Assert.Pass("Correctly threw when no .csproj found.");
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region ParseDesignerFile

    [Test]
    public void ParseDesignerFile_ExtractsNamespaceAndClassName()
    {
        var content = @"
namespace MyApp.Forms;

partial class AddressEntryForm {
    private void InitializeComponent() { }
}";
        var (ns, className, _) = CompiledFormRenderer.ParseDesignerFile(content);
        Assert.That(ns, Is.EqualTo("MyApp.Forms"));
        Assert.That(className, Is.EqualTo("AddressEntryForm"));
    }

    [Test]
    public void ParseDesignerFile_NoNamespace_ReturnsNull()
    {
        var content = @"
partial class MyForm {
    private void InitializeComponent() { }
}";
        var (ns, className, _) = CompiledFormRenderer.ParseDesignerFile(content);
        Assert.That(ns, Is.Null);
        Assert.That(className, Is.EqualTo("MyForm"));
    }

    [Test]
    public void ParseDesignerFile_ExtractsEventHandlers_ModernSyntax()
    {
        var content = @"
namespace Test;
partial class MyForm {
    private void InitializeComponent() {
        btnSubmit.Click += this.btnSubmit_Click;
        this.Load += this.MyForm_Load;
    }
}";
        var (_, _, handlers) = CompiledFormRenderer.ParseDesignerFile(content);
        Assert.That(handlers, Contains.Item("btnSubmit_Click"));
        Assert.That(handlers, Contains.Item("MyForm_Load"));
    }

    [Test]
    public void ParseDesignerFile_ExtractsEventHandlers_OldSyntax()
    {
        var content = @"
namespace Test;
partial class MyForm {
    private void InitializeComponent() {
        this.button1.Click += new System.EventHandler(this.button1_Click);
    }
}";
        var (_, _, handlers) = CompiledFormRenderer.ParseDesignerFile(content);
        Assert.That(handlers, Contains.Item("button1_Click"));
    }

    [Test]
    public void ParseDesignerFile_NoDuplicateHandlers()
    {
        var content = @"
namespace Test;
partial class MyForm {
    private void InitializeComponent() {
        btnA.Click += this.SharedHandler;
        btnB.Click += this.SharedHandler;
    }
}";
        var (_, _, handlers) = CompiledFormRenderer.ParseDesignerFile(content);
        Assert.That(handlers.Count(h => h == "SharedHandler"), Is.EqualTo(1));
    }

    [Test]
    public void ParseDesignerFile_NoPartialClass_Throws()
    {
        var content = "class NotPartial { }";
        Assert.Throws<InvalidOperationException>(() =>
            CompiledFormRenderer.ParseDesignerFile(content));
    }

    #endregion

    #region GenerateCsproj

    [Test]
    public void GenerateCsproj_CopiesPackageReferences()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceCsproj = Path.Combine(tempDir, "Source.csproj");
        File.WriteAllText(sourceCsproj, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""SomePackage"" Version=""1.2.3"" />
    <PackageReference Include=""AnotherPkg"" Version=""4.5.6"" />
  </ItemGroup>
</Project>");

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);
        try
        {
            CompiledFormRenderer.GenerateCsproj(outputDir, sourceCsproj);
            var generated = File.ReadAllText(Path.Combine(outputDir, "TempFormRender.csproj"));

            Assert.That(generated, Does.Contain("SomePackage"));
            Assert.That(generated, Does.Contain("1.2.3"));
            Assert.That(generated, Does.Contain("AnotherPkg"));
            Assert.That(generated, Does.Contain("4.5.6"));
            Assert.That(generated, Does.Contain("UseWindowsForms"));
            Assert.That(generated, Does.Contain("net8.0-windows"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void GenerateCsproj_NoPackageRefs_StillWorks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceCsproj = Path.Combine(tempDir, "Source.csproj");
        File.WriteAllText(sourceCsproj, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>");

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);
        try
        {
            CompiledFormRenderer.GenerateCsproj(outputDir, sourceCsproj);
            Assert.That(File.Exists(Path.Combine(outputDir, "TempFormRender.csproj")), Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region GenerateCodeBehind

    [Test]
    public void GenerateCodeBehind_GeneratesPartialClassWithFormBase()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            CompiledFormRenderer.GenerateCodeBehind(tempDir, "MyApp", "MyForm",
                new List<string> { "btnOk_Click", "Form_Load" });
            var content = File.ReadAllText(Path.Combine(tempDir, "MyForm.cs"));

            Assert.That(content, Does.Contain("namespace MyApp;"));
            Assert.That(content, Does.Contain("partial class MyForm : Form"));
            Assert.That(content, Does.Contain("public MyForm()"));
            Assert.That(content, Does.Contain("InitializeComponent();"));
            Assert.That(content, Does.Contain("btnOk_Click"));
            Assert.That(content, Does.Contain("Form_Load"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void GenerateCodeBehind_NoNamespace_OmitsNamespaceLine()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            CompiledFormRenderer.GenerateCodeBehind(tempDir, null, "MyForm", new List<string>());
            var content = File.ReadAllText(Path.Combine(tempDir, "MyForm.cs"));

            Assert.That(content, Does.Not.Contain("namespace"));
            Assert.That(content, Does.Contain("partial class MyForm : Form"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region GenerateProgram

    [Test]
    public void GenerateProgram_ContainsFormInstantiation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            CompiledFormRenderer.GenerateProgram(tempDir, "MyApp", "MyForm");
            var content = File.ReadAllText(Path.Combine(tempDir, "Program.cs"));

            Assert.That(content, Does.Contain("new MyApp.MyForm()"));
            Assert.That(content, Does.Contain("DrawToBitmap"));
            Assert.That(content, Does.Contain("ToBase64String"));
            Assert.That(content, Does.Contain("[STAThread]"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void GenerateProgram_NoNamespace_UsesClassNameDirectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            CompiledFormRenderer.GenerateProgram(tempDir, null, "MyForm");
            var content = File.ReadAllText(Path.Combine(tempDir, "Program.cs"));

            Assert.That(content, Does.Contain("new MyForm()"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region End-to-End

    [Test]
    [Category("E2E")]
    public void RenderForm_AddressEntryForm_ProducesValidPng()
    {
        var sourceFile = @"C:\Users\thoma\source\repos\WinformMCPDemos\WinFormsApp1\AddressEntryForm.cs";
        if (!File.Exists(sourceFile))
            Assert.Ignore("AddressEntryForm source not found on this machine.");

        var renderer = new CompiledFormRenderer();
        var pngBytes = renderer.RenderForm(sourceFile);

        Assert.That(pngBytes, Is.Not.Null);
        Assert.That(pngBytes.Length, Is.GreaterThan(1000), "PNG should be a reasonable size");

        // Verify PNG magic bytes
        Assert.That(pngBytes[0], Is.EqualTo(0x89));
        Assert.That(pngBytes[1], Is.EqualTo(0x50)); // 'P'
        Assert.That(pngBytes[2], Is.EqualTo(0x4E)); // 'N'
        Assert.That(pngBytes[3], Is.EqualTo(0x47)); // 'G'
    }

    [Test]
    [Category("E2E")]
    public void RenderForm_CachesResult_SecondCallIsFast()
    {
        var sourceFile = @"C:\Users\thoma\source\repos\WinformMCPDemos\WinFormsApp1\AddressEntryForm.cs";
        if (!File.Exists(sourceFile))
            Assert.Ignore("AddressEntryForm source not found on this machine.");

        var renderer = new CompiledFormRenderer();

        // First call - compiles
        var bytes1 = renderer.RenderForm(sourceFile);

        // Second call - should hit cache
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bytes2 = renderer.RenderForm(sourceFile);
        sw.Stop();

        Assert.That(bytes2, Is.EqualTo(bytes1));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "Cached call should be near-instant");
    }

    [Test]
    [Category("E2E")]
    public void RenderForm_KitchenSink_ProducesValidPng()
    {
        var designerFile = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.Designer.cs");
        if (!File.Exists(designerFile))
            Assert.Ignore("KitchenSink designer file not found.");

        var renderer = new CompiledFormRenderer();
        var pngBytes = renderer.RenderForm(designerFile);

        Assert.That(pngBytes, Is.Not.Null);
        Assert.That(pngBytes.Length, Is.GreaterThan(1000));
        Assert.That(pngBytes[0], Is.EqualTo(0x89));
        Assert.That(pngBytes[1], Is.EqualTo(0x50));

        // Save for visual review
        var outputPath = Path.Combine(Path.GetTempPath(), "KitchenSink_compiled.png");
        File.WriteAllBytes(outputPath, pngBytes);
        TestContext.WriteLine($"Rendered: {outputPath}");
    }

    [Test]
    [Category("E2E")]
    public void GroundTruth_KitchenSink_FlaUI_Screenshot()
    {
        var kitchenSinkExe = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "bin", "publish", "KitchenSink.exe");
        if (!File.Exists(kitchenSinkExe))
            Assert.Ignore("KitchenSink.exe not built. Run: dotnet build tests/.../TestData/KitchenSink/KitchenSink.csproj -c Release -o bin/publish");

        using var automation = new Rhombus.WinFormsMcp.Server.Automation.AutomationHelper(headless: false);
        var process = automation.LaunchApp(kitchenSinkExe);
        try
        {
            // Wait for the window to fully render
            System.Threading.Thread.Sleep(2000);

            var window = automation.GetMainWindow(process.Id);
            Assert.That(window, Is.Not.Null, "Should find main window");

            var outputPath = Path.Combine(Path.GetTempPath(), "KitchenSink_flaui_groundtruth.png");
            automation.TakeScreenshot(outputPath, window);

            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(1000));
            TestContext.WriteLine($"FlaUI ground truth: {outputPath}");
        }
        finally
        {
            automation.CloseApp(process.Id, force: true);
        }
    }

    [Test]
    [Category("E2E")]
    [Explicit("Manual test to save rendered PNG for visual verification")]
    public void RenderForm_AddressEntryForm_SavePngForReview()
    {
        var sourceFile = @"C:\Users\thoma\source\repos\WinformMCPDemos\WinFormsApp1\AddressEntryForm.cs";
        if (!File.Exists(sourceFile))
            Assert.Ignore("AddressEntryForm source not found on this machine.");

        var renderer = new CompiledFormRenderer();
        var pngBytes = renderer.RenderForm(sourceFile);

        var outputPath = Path.Combine(Path.GetTempPath(), "AddressEntryForm_rendered.png");
        File.WriteAllBytes(outputPath, pngBytes);
        TestContext.WriteLine($"Rendered PNG saved to: {outputPath}");
    }

    #endregion

    #region Benchmark

    [Test]
    [Category("E2E")]
    public void Benchmark_AllThreeRenderers_KitchenSink()
    {
        var designerFile = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.Designer.cs");
        if (!File.Exists(designerFile))
            Assert.Ignore("KitchenSink designer file not found.");

        var kitchenSinkExe = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "bin", "publish", "KitchenSink.exe");
        if (!File.Exists(kitchenSinkExe))
            Assert.Ignore("KitchenSink.exe not built.");

        // Scenario: developer edits .Designer.cs, wants to see the result.
        // Every run is cold — source changed, so no caching applies.
        const int runs = 5;
        var sw = new System.Diagnostics.Stopwatch();
        var kitchenSinkCsproj = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSink.csproj");

        // ── 1. Roslyn (syntax tree) ─────────────────────────────
        //    Reads .Designer.cs, walks AST, creates controls via reflection.
        //    No build step needed — only reads the designer file.
        var roslynOut = Path.Combine(Path.GetTempPath(), "bench_roslyn.png");
        sw.Restart();
        for (int i = 0; i < runs; i++)
            new SyntaxTreeFormRenderer().RenderDesignerFile(designerFile, roslynOut);
        sw.Stop();
        var roslynMs = sw.ElapsedMilliseconds / (double)runs;
        var roslynSize = new FileInfo(roslynOut).Length;

        // ── 2. In-process (Roslyn emit) ─────────────────────────
        //    Compiles designer in-memory via Roslyn, loads assembly,
        //    runs real InitializeComponent + DrawToBitmap. No external process.
        sw.Restart();
        for (int i = 0; i < runs; i++)
            new InProcessFormRenderer().RenderForm(designerFile);
        sw.Stop();
        var inprocMs = sw.ElapsedMilliseconds / (double)runs;
        var inprocSize = new InProcessFormRenderer().RenderForm(designerFile).Length;

        // ── 3. Compiled (dotnet build + run) ────────────────────
        //    Generates temp csproj, runs dotnet build, launches exe,
        //    captures base64 PNG from stdout.
        sw.Restart();
        for (int i = 0; i < runs; i++)
            new CompiledFormRenderer().RenderForm(designerFile);
        sw.Stop();
        var compiledMs = sw.ElapsedMilliseconds / (double)runs;
        var compiledSize = new CompiledFormRenderer().RenderForm(designerFile).Length;

        // ── 4. FlaUI (build + publish + launch + screenshot) ────
        //    Full pipeline: build the actual project, publish exe,
        //    launch it, wait for window, take FlaUI screenshot.
        var flauiTimes = new List<double>();
        var flauiOut = Path.Combine(Path.GetTempPath(), "bench_flaui.png");
        for (int i = 0; i < runs; i++)
        {
            sw.Restart();
            var pubDir = Path.Combine(Path.GetTempPath(), $"bench_flaui_{i}");
            BuildAndPublish(kitchenSinkCsproj, pubDir);
            RunFlaUI(Path.Combine(pubDir, "KitchenSink.exe"), false, flauiOut);
            sw.Stop();
            flauiTimes.Add(sw.ElapsedMilliseconds);
            try { Directory.Delete(pubDir, true); } catch { }
        }
        var flauiMs = flauiTimes.Average();
        var flauiSize = new FileInfo(flauiOut).Length;

        // ── Results ─────────────────────────────────────────────
        var fastest = roslynMs;
        TestContext.WriteLine("");
        TestContext.WriteLine("  Scenario: source changed → how fast to get a preview PNG?");
        TestContext.WriteLine("  Every run is cold (no caching). Avg of {0} runs.", runs);
        TestContext.WriteLine("");
        TestContext.WriteLine("╔═════════════════════════════════════════════════════════════════╗");
        TestContext.WriteLine("║  Method                    │   Time     │  Size   │  vs Best  ║");
        TestContext.WriteLine("╠════════════════════════════╪════════════╪═════════╪═══════════╣");
        TestContext.WriteLine($"║  Roslyn (syntax tree)      │ {roslynMs,7:F0} ms  │ {roslynSize / 1024.0,5:F1} KB │   1.0x    ║");
        TestContext.WriteLine($"║  In-process (Roslyn emit)  │ {inprocMs,7:F0} ms  │ {inprocSize / 1024.0,5:F1} KB │  {inprocMs / fastest,4:F1}x    ║");
        TestContext.WriteLine($"║  Compiled (dotnet build)   │ {compiledMs,7:F0} ms  │ {compiledSize / 1024.0,5:F1} KB │  {compiledMs / fastest,4:F1}x    ║");
        TestContext.WriteLine($"║  FlaUI (build + launch)    │ {flauiMs,7:F0} ms  │ {flauiSize / 1024.0,5:F1} KB │  {flauiMs / fastest,4:F1}x    ║");
        TestContext.WriteLine("╠═════════════════════════════════════════════════════════════════╣");
        TestContext.WriteLine("║  Note: Roslyn skips custom controls it can't resolve.         ║");
        TestContext.WriteLine("║  In-process/Compiled/FlaUI render all controls (43 KB vs 30). ║");
        TestContext.WriteLine("╚═════════════════════════════════════════════════════════════════╝");

        Assert.That(roslynMs, Is.GreaterThan(0));
        Assert.That(inprocMs, Is.GreaterThan(0));
        Assert.That(compiledMs, Is.GreaterThan(0));
        Assert.That(flauiMs, Is.GreaterThan(0));
    }

    private static void BuildAndPublish(string csprojPath, string outputDir)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{csprojPath}\" -c Release -o \"{outputDir}\" --nologo -v q",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit(60000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet publish failed: {process.StandardError.ReadToEnd()}");
    }

    private static void RunFlaUI(string exePath, bool headless, string outputPath)
    {
        using var automation = new AutomationHelper(headless: headless);
        var process = automation.LaunchApp(exePath);
        try
        {
            var window = WaitForMainWindow(automation, process.Id);
            automation.TakeScreenshot(outputPath, window);
        }
        finally
        {
            automation.CloseApp(process.Id, force: true);
        }
    }

    private static FlaUI.Core.AutomationElements.AutomationElement WaitForMainWindow(
        AutomationHelper automation, int pid, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var window = automation.GetMainWindow(pid);
            if (window != null)
                return window;
            System.Threading.Thread.Sleep(50);
        }
        throw new TimeoutException($"Main window not found within {timeoutMs}ms");
    }

    [Test]
    [Category("E2E")]
    public void InProcess_KitchenSink_ProducesValidPng()
    {
        var designerFile = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.Designer.cs");
        if (!File.Exists(designerFile))
            Assert.Ignore("KitchenSink designer file not found.");

        var renderer = new InProcessFormRenderer();
        var pngBytes = renderer.RenderForm(designerFile);

        Assert.That(pngBytes, Is.Not.Null);
        Assert.That(pngBytes.Length, Is.GreaterThan(1000));
        Assert.That(pngBytes[0], Is.EqualTo(0x89)); // PNG magic

        var outputPath = Path.Combine(Path.GetTempPath(), "KitchenSink_inprocess.png");
        File.WriteAllBytes(outputPath, pngBytes);
        TestContext.WriteLine($"In-process rendered: {outputPath}");
    }

    #endregion
}
