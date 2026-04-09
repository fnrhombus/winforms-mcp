using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Tests;

[TestFixture]
public class RendererProcessPoolTests {
    #region TFM Mapping

    [TestCase("net48", "net48")]
    [TestCase("net472", "net48")]
    [TestCase("net471", "net48")]
    [TestCase("net47", "net48")]
    [TestCase("net462", "net48")]
    [TestCase("net461", "net48")]
    [TestCase("net46", "net48")]
    [TestCase("net452", "net48")]
    [TestCase("net45", "net48")]
    [TestCase("net40", "net48")]
    [TestCase("net481", "net48")]
    public void MapToHostTfm_NetFramework_MapsToNet48(string input, string expected) {
        Assert.That(RendererProcessPool.MapToHostTfm(input), Is.EqualTo(expected));
    }

    [TestCase("netcoreapp3.0", "netcoreapp3.1")]
    [TestCase("netcoreapp3.1", "netcoreapp3.1")]
    public void MapToHostTfm_NetCore3_MapsToNetCoreApp31(string input, string expected) {
        Assert.That(RendererProcessPool.MapToHostTfm(input), Is.EqualTo(expected));
    }

    [TestCase("net5.0-windows", "net8.0-windows")]
    [TestCase("net6.0-windows", "net8.0-windows")]
    [TestCase("net7.0-windows", "net8.0-windows")]
    [TestCase("net8.0-windows", "net8.0-windows")]
    [TestCase("net9.0-windows", "net8.0-windows")]
    public void MapToHostTfm_Net5Plus_MapsToNet80Windows(string input, string expected) {
        Assert.That(RendererProcessPool.MapToHostTfm(input), Is.EqualTo(expected));
    }

    [TestCase("netcoreapp2.1", "net8.0-windows")]
    [TestCase("something-unknown", "net8.0-windows")]
    public void MapToHostTfm_UnknownFallsToNet8(string input, string expected) {
        Assert.That(RendererProcessPool.MapToHostTfm(input), Is.EqualTo(expected));
    }

    [Test]
    public void MapToHostTfm_CaseInsensitive() {
        Assert.That(RendererProcessPool.MapToHostTfm("NET48"), Is.EqualTo("net48"));
        Assert.That(RendererProcessPool.MapToHostTfm("Net8.0-Windows"), Is.EqualTo("net8.0-windows"));
    }

    #endregion

    #region TFM Detection from csproj

    [Test]
    public void DetectTfmFromCsproj_SingleTarget() {
        var csproj = Path.GetTempFileName();
        try {
            File.WriteAllText(csproj, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
  </PropertyGroup>
</Project>");
            var tfm = RendererProcessPool.DetectTfmFromCsproj(csproj);
            Assert.That(tfm, Is.EqualTo("net8.0-windows"));
        }
        finally {
            File.Delete(csproj);
        }
    }

    [Test]
    public void DetectTfmFromCsproj_MultiTarget_ReturnsFirst() {
        var csproj = Path.GetTempFileName();
        try {
            File.WriteAllText(csproj, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net48;net8.0-windows</TargetFrameworks>
  </PropertyGroup>
</Project>");
            var tfm = RendererProcessPool.DetectTfmFromCsproj(csproj);
            Assert.That(tfm, Is.EqualTo("net48"));
        }
        finally {
            File.Delete(csproj);
        }
    }

    [Test]
    public void DetectTfmFromCsproj_OldStyleTargetFrameworkVersion() {
        var csproj = Path.GetTempFileName();
        try {
            File.WriteAllText(csproj, @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <OutputType>WinExe</OutputType>
  </PropertyGroup>
</Project>");
            var tfm = RendererProcessPool.DetectTfmFromCsproj(csproj);
            Assert.That(tfm, Is.EqualTo("net472"));
        }
        finally {
            File.Delete(csproj);
        }
    }

    [Test]
    public void DetectTfmFromCsproj_OldStyleTargetFrameworkVersion_v461() {
        var csproj = Path.GetTempFileName();
        try {
            File.WriteAllText(csproj, @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion>
  </PropertyGroup>
</Project>");
            var tfm = RendererProcessPool.DetectTfmFromCsproj(csproj);
            Assert.That(tfm, Is.EqualTo("net461"));
        }
        finally {
            File.Delete(csproj);
        }
    }

    [Test]
    public void DetectTfmFromCsproj_NoTargetFramework_Throws() {
        var csproj = Path.GetTempFileName();
        try {
            File.WriteAllText(csproj, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>");
            Assert.Throws<InvalidOperationException>(() => RendererProcessPool.DetectTfmFromCsproj(csproj));
        }
        finally {
            File.Delete(csproj);
        }
    }

    #endregion

    #region GetConfiguredTfm

    [Test]
    public void GetConfiguredTfm_WhenNotSet_ReturnsAuto() {
        var original = Environment.GetEnvironmentVariable("TFM");
        try {
            Environment.SetEnvironmentVariable("TFM", null);
            Assert.That(RendererProcessPool.GetConfiguredTfm(), Is.EqualTo("auto"));
        }
        finally {
            Environment.SetEnvironmentVariable("TFM", original);
        }
    }

    [Test]
    public void GetConfiguredTfm_WhenSet_ReturnsValue() {
        var original = Environment.GetEnvironmentVariable("TFM");
        try {
            Environment.SetEnvironmentVariable("TFM", "net48");
            Assert.That(RendererProcessPool.GetConfiguredTfm(), Is.EqualTo("net48"));
        }
        finally {
            Environment.SetEnvironmentVariable("TFM", original);
        }
    }

    [Test]
    public void GetConfiguredTfm_WhenEmpty_ReturnsAuto() {
        var original = Environment.GetEnvironmentVariable("TFM");
        try {
            Environment.SetEnvironmentVariable("TFM", "  ");
            Assert.That(RendererProcessPool.GetConfiguredTfm(), Is.EqualTo("auto"));
        }
        finally {
            Environment.SetEnvironmentVariable("TFM", original);
        }
    }

    #endregion

    #region Pool Lifecycle

    [Test]
    public void Dispose_DoesNotThrow() {
        var pool = new RendererProcessPool("/nonexistent");
        Assert.DoesNotThrow(() => pool.Dispose());
    }

    [Test]
    public void RenderAsync_AfterDispose_ThrowsObjectDisposed() {
        var pool = new RendererProcessPool("/nonexistent");
        pool.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await pool.RenderAsync("content", null, null, "net8.0-windows"));
    }

    [Test]
    public void RenderAsync_AutoWithoutCsproj_ThrowsArgument() {
        using var pool = new RendererProcessPool("/nonexistent");
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await pool.RenderAsync("content", null, null, "auto"));
        Assert.That(ex!.Message, Does.Contain("csprojPath is required"));
    }

    #endregion

    #region E2E Process Pool Rendering

    [Test]
    public async Task RenderAsync_Net8Host_ProducesValidPng() {
        // Find the RendererHost build output
        var serverDir = Path.GetDirectoryName(typeof(RendererProcessPool).Assembly.Location)!;
        // Navigate from test output to source layout
        // Test: tests/.../bin/Debug/net8.0-windows/
        // Host: src/Rhombus.WinFormsMcp.RendererHost/bin/Debug/
        var repoRoot = FindRepoRoot(serverDir);
        var hostBasePath = Path.Combine(repoRoot, "src", "Rhombus.WinFormsMcp.RendererHost", "bin", "Debug");

        if (!Directory.Exists(Path.Combine(hostBasePath, "net8.0-windows"))) {
            Assert.Ignore("RendererHost not built. Run: dotnet build src/Rhombus.WinFormsMcp.RendererHost");
        }

        using var pool = new RendererProcessPool(hostBasePath);

        var designerContent = @"
namespace TestApp {
    partial class Form1 {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) { components.Dispose(); }
            base.Dispose(disposing);
        }
        private void InitializeComponent() {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(400, 300);
            this.Name = ""Form1"";
            this.Text = ""Test Form"";
            this.ResumeLayout(false);
        }
    }
}";

        var pngBytes = await pool.RenderAsync(designerContent, null, null, "net8.0-windows");

        // Verify PNG magic bytes
        Assert.That(pngBytes.Length, Is.GreaterThan(8));
        Assert.That(pngBytes[0], Is.EqualTo(0x89));
        Assert.That(pngBytes[1], Is.EqualTo(0x50)); // P
        Assert.That(pngBytes[2], Is.EqualTo(0x4E)); // N
        Assert.That(pngBytes[3], Is.EqualTo(0x47)); // G
    }

    [Test]
    public async Task RenderAsync_ProcessReuse_SecondCallFaster() {
        var repoRoot = FindRepoRoot(Path.GetDirectoryName(typeof(RendererProcessPool).Assembly.Location)!);
        var hostBasePath = Path.Combine(repoRoot, "src", "Rhombus.WinFormsMcp.RendererHost", "bin", "Debug");

        if (!Directory.Exists(Path.Combine(hostBasePath, "net8.0-windows"))) {
            Assert.Ignore("RendererHost not built.");
        }

        using var pool = new RendererProcessPool(hostBasePath);

        var designerContent = @"
namespace TestApp {
    partial class Form1 {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) { components.Dispose(); }
            base.Dispose(disposing);
        }
        private void InitializeComponent() {
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(200, 150);
            this.Name = ""Form1"";
            this.Text = ""Reuse Test"";
            this.ResumeLayout(false);
        }
    }
}";

        // First call: cold start (spawns process)
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        await pool.RenderAsync(designerContent, null, null, "net8.0-windows");
        sw1.Stop();

        // Second call: warm (reuses process)
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        await pool.RenderAsync(designerContent, null, null, "net8.0-windows");
        sw2.Stop();

        TestContext.WriteLine($"Cold start: {sw1.ElapsedMilliseconds}ms, Warm: {sw2.ElapsedMilliseconds}ms");

        // Warm call should be significantly faster (no process spawn overhead)
        Assert.That(sw2.ElapsedMilliseconds, Is.LessThan(sw1.ElapsedMilliseconds),
            $"Warm call ({sw2.ElapsedMilliseconds}ms) should be faster than cold start ({sw1.ElapsedMilliseconds}ms)");
    }

    private static string FindRepoRoot(string startDir) {
        var dir = startDir;
        while (dir != null) {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not find repository root from " + startDir);
    }

    #endregion
}
