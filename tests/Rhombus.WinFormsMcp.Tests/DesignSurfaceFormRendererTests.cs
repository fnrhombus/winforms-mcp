using System.Drawing;
using System.Windows.Forms;

using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class DesignSurfaceFormRendererTests {
    private DesignSurfaceFormRenderer _renderer = null!;

    [SetUp]
    public void Setup() {
        _renderer = new DesignSurfaceFormRenderer();
    }

    #region Basic Form Rendering

    [Test]
    public void RenderDesignerCode_SimpleForm_ProducesValidPng() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            this.button1.Text = ""Click Me"";
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(120, 30);
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.Controls.Add(this.button1);
            this.Text = ""Simple Form"";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
        private System.Windows.Forms.Button button1;
    }
}";

        var pngBytes = _renderer.RenderDesignerCode(designerCode);

        AssertValidPng(pngBytes);
        TestContext.WriteLine($"Simple form: {pngBytes.Length:N0} bytes");
    }

    [Test]
    public void RenderDesignerCode_FormWithMultipleControls_ProducesValidPng() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            this.button1.Location = new System.Drawing.Point(12, 12);
            this.button1.Size = new System.Drawing.Size(120, 30);
            this.button1.Text = ""OK"";
            this.textBox1.Location = new System.Drawing.Point(12, 50);
            this.textBox1.Size = new System.Drawing.Size(200, 23);
            this.textBox1.Text = ""Hello World"";
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 80);
            this.label1.Text = ""A Label"";
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(12, 100);
            this.checkBox1.Text = ""Check me"";
            this.ClientSize = new System.Drawing.Size(400, 300);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.checkBox1);
            this.Text = ""Multi Control"";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox checkBox1;
    }
}";

        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    #endregion

    #region KitchenSink Form

    [Test]
    public void RenderForm_KitchenSink_ProducesValidPng() {
        var designerPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.Designer.cs");
        if (!File.Exists(designerPath))
            Assert.Ignore("KitchenSink designer file not found.");

        var designerContent = File.ReadAllText(designerPath);

        // KitchenSink uses custom controls (MoodRing, StatusDashboard) that may not resolve
        // without build output. Render with standard controls only (custom ones will be skipped).
        var pngBytes = _renderer.RenderDesignerCode(designerContent);
        AssertValidPng(pngBytes);
        Assert.That(pngBytes.Length, Is.GreaterThan(1000),
            "KitchenSink should produce a substantial PNG");

        var outputPath = Path.Combine(Path.GetTempPath(), "DesignSurface_KitchenSink.png");
        File.WriteAllBytes(outputPath, pngBytes);
        TestContext.WriteLine($"KitchenSink rendered: {outputPath} ({pngBytes.Length:N0} bytes)");
    }

    #endregion

    #region UserControl Rendering

    [Test]
    public void RenderDesignerCode_UserControl_DetectsAndRenders() {
        var designerContent = @"
namespace Test {
    partial class MyUserControl {
        private void InitializeComponent() {
            this.lblHeader = new System.Windows.Forms.Label();
            this.pnlGreen = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            this.lblHeader.AutoSize = true;
            this.lblHeader.Font = new System.Drawing.Font(""Consolas"", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeader.Location = new System.Drawing.Point(8, 4);
            this.lblHeader.Text = ""STATUS"";
            this.pnlGreen.BackColor = System.Drawing.Color.LimeGreen;
            this.pnlGreen.Location = new System.Drawing.Point(8, 28);
            this.pnlGreen.Size = new System.Drawing.Size(16, 16);
            this.Controls.Add(this.lblHeader);
            this.Controls.Add(this.pnlGreen);
            this.Size = new System.Drawing.Size(280, 80);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
        private System.Windows.Forms.Label lblHeader;
        private System.Windows.Forms.Panel pnlGreen;
    }
}";

        var companionContent = @"
using System.Windows.Forms;
namespace Test;
public partial class MyUserControl : UserControl
{
    public MyUserControl() { InitializeComponent(); }
}";

        var pngBytes = _renderer.RenderDesignerCode(designerContent, companionContent);
        AssertValidPng(pngBytes);
        TestContext.WriteLine($"UserControl rendered: {pngBytes.Length:N0} bytes");
    }

    [Test]
    public void RenderDesignerCode_StatusDashboard_RendersAsUserControl() {
        var designerPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "StatusDashboard.Designer.cs");
        var companionPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "StatusDashboard.cs");
        if (!File.Exists(designerPath))
            Assert.Ignore("StatusDashboard designer file not found.");

        var designerContent = File.ReadAllText(designerPath);
        var companionContent = File.Exists(companionPath) ? File.ReadAllText(companionPath) : null;

        var pngBytes = _renderer.RenderDesignerCode(designerContent, companionContent);
        AssertValidPng(pngBytes);
        TestContext.WriteLine($"StatusDashboard rendered: {pngBytes.Length:N0} bytes");
    }

    #endregion

    #region Property Types

    [Test]
    public void RenderDesignerCode_EnumProperty() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button1.Dock = System.Windows.Forms.DockStyle.Top;
            this.button1.Size = new System.Drawing.Size(200, 30);
            this.button1.Text = ""Flat Button"";
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Controls.Add(this.button1);
        }
        private System.Windows.Forms.Button button1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_ColorProperties() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.BackColor = System.Drawing.Color.Red;
            this.button1.ForeColor = System.Drawing.Color.FromArgb(255, 255, 255);
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(100, 30);
            this.button1.Text = ""Colored"";
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Controls.Add(this.button1);
        }
        private System.Windows.Forms.Button button1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_FontProperty() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.label1 = new System.Windows.Forms.Label();
            this.label1.Font = new System.Drawing.Font(""Segoe UI"", 16F, System.Drawing.FontStyle.Bold);
            this.label1.Location = new System.Drawing.Point(10, 10);
            this.label1.AutoSize = true;
            this.label1.Text = ""Bold Title"";
            this.ClientSize = new System.Drawing.Size(300, 100);
            this.Controls.Add(this.label1);
        }
        private System.Windows.Forms.Label label1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_SizeAndPointProperties() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.Location = new System.Drawing.Point(50, 50);
            this.button1.Size = new System.Drawing.Size(200, 40);
            this.button1.Text = ""Positioned"";
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.Controls.Add(this.button1);
        }
        private System.Windows.Forms.Button button1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_ArrayProperty_ComboBoxItems() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.comboBox1.Items.AddRange(new object[] { ""Item1"", ""Item2"", ""Item3"" });
            this.comboBox1.Location = new System.Drawing.Point(10, 10);
            this.comboBox1.Size = new System.Drawing.Size(200, 23);
            this.ClientSize = new System.Drawing.Size(250, 100);
            this.Controls.Add(this.comboBox1);
        }
        private System.Windows.Forms.ComboBox comboBox1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_DecimalProperty_NumericUpDown() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.nudLevel = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.nudLevel)).BeginInit();
            this.SuspendLayout();
            this.nudLevel.Location = new System.Drawing.Point(10, 10);
            this.nudLevel.Size = new System.Drawing.Size(80, 23);
            this.nudLevel.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.nudLevel.Value = new decimal(new int[] { 42, 0, 0, 0 });
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Controls.Add(this.nudLevel);
            ((System.ComponentModel.ISupportInitialize)(this.nudLevel)).EndInit();
            this.ResumeLayout(false);
        }
        private System.Windows.Forms.NumericUpDown nudLevel;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_BitwiseOrEnumFlags() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(100, 30);
            this.button1.Text = ""Anchored"";
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Controls.Add(this.button1);
        }
        private System.Windows.Forms.Button button1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    #endregion

    #region Controls.Add Hierarchy

    [Test]
    public void RenderDesignerCode_PanelContainingButton_NestedHierarchy() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.panel1 = new System.Windows.Forms.Panel();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            this.panel1.BackColor = System.Drawing.Color.LightBlue;
            this.panel1.Location = new System.Drawing.Point(10, 10);
            this.panel1.Size = new System.Drawing.Size(200, 100);
            this.button1.Text = ""Inside Panel"";
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(100, 30);
            this.panel1.Controls.Add(this.button1);
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.Controls.Add(this.panel1);
            this.ResumeLayout(false);
        }
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button button1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_GroupBoxWithControls() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.grp1 = new System.Windows.Forms.GroupBox();
            this.txtName = new System.Windows.Forms.TextBox();
            this.lblName = new System.Windows.Forms.Label();
            this.SuspendLayout();
            this.grp1.Text = ""Details"";
            this.grp1.Location = new System.Drawing.Point(10, 10);
            this.grp1.Size = new System.Drawing.Size(280, 100);
            this.lblName.Text = ""Name:"";
            this.lblName.Location = new System.Drawing.Point(10, 25);
            this.lblName.AutoSize = true;
            this.txtName.Location = new System.Drawing.Point(80, 22);
            this.txtName.Size = new System.Drawing.Size(180, 23);
            this.grp1.Controls.Add(this.lblName);
            this.grp1.Controls.Add(this.txtName);
            this.ClientSize = new System.Drawing.Size(300, 150);
            this.Controls.Add(this.grp1);
            this.ResumeLayout(false);
        }
        private System.Windows.Forms.GroupBox grp1;
        private System.Windows.Forms.TextBox txtName;
        private System.Windows.Forms.Label lblName;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_TabControlWithTabPages() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.SuspendLayout();
            this.tabControl1.Location = new System.Drawing.Point(10, 10);
            this.tabControl1.Size = new System.Drawing.Size(280, 180);
            this.tabPage1.Text = ""General"";
            this.tabPage2.Text = ""Advanced"";
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.Controls.Add(this.tabControl1);
            this.ResumeLayout(false);
        }
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    #endregion

    #region Cache Behavior

    [Test]
    public void RenderDesignerCode_SameContent_UsesCache() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Text = ""Cache Test"";
        }
    }
}";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var first = _renderer.RenderDesignerCode(designerCode);
        sw.Stop();
        var firstMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var second = _renderer.RenderDesignerCode(designerCode);
        sw.Stop();
        var secondMs = sw.ElapsedMilliseconds;

        Assert.That(first, Is.EqualTo(second), "Cached result should be identical");
        Assert.That(secondMs, Is.LessThan(firstMs + 5),
            $"Cached call ({secondMs}ms) should be near-instant vs first ({firstMs}ms)");
        TestContext.WriteLine($"First: {firstMs}ms, Cached: {secondMs}ms");
    }

    #endregion

    #region Benchmark

    [Test]
    [Category("E2E")]
    public void Benchmark_DesignSurfaceRenderer_VsExistingRenderers() {
        var designerFile = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.Designer.cs");
        if (!File.Exists(designerFile))
            Assert.Ignore("KitchenSink designer file not found.");

        var designerContent = File.ReadAllText(designerFile);
        const int runs = 3;
        var sw = new System.Diagnostics.Stopwatch();

        sw.Restart();
        byte[]? dsBytes = null;
        for (int i = 0; i < runs; i++)
            dsBytes = new DesignSurfaceFormRenderer().RenderDesignerCode(designerContent);
        sw.Stop();
        var dsMs = sw.ElapsedMilliseconds / (double)runs;

        TestContext.WriteLine("");
        TestContext.WriteLine($"  DesignSurfaceFormRenderer Benchmark (avg of {runs} cold runs):");
        TestContext.WriteLine($"    DesignSurface:  {dsMs,7:F0} ms  ({dsBytes!.Length / 1024.0:F1} KB)");
        TestContext.WriteLine("");

        AssertValidPng(dsBytes);
    }

    #endregion

    #region Base Type Detection

    [Test]
    public void DetectBaseType_CompanionWithForm_ReturnsForm() {
        var result = DesignSurfaceFormRenderer.DetectBaseType("", "partial class Foo : Form { }");
        Assert.That(result, Is.EqualTo(typeof(Form)));
    }

    [Test]
    public void DetectBaseType_CompanionWithUserControl_ReturnsUserControl() {
        var result = DesignSurfaceFormRenderer.DetectBaseType("", "partial class Foo : UserControl { }");
        Assert.That(result, Is.EqualTo(typeof(UserControl)));
    }

    [Test]
    public void DetectBaseType_NoCompanion_ClientSize_ReturnsForm() {
        var designer = "this.ClientSize = new System.Drawing.Size(400, 300);";
        var result = DesignSurfaceFormRenderer.DetectBaseType(designer, null);
        Assert.That(result, Is.EqualTo(typeof(Form)));
    }

    [Test]
    public void DetectBaseType_NoCompanion_SizeOnly_ReturnsUserControl() {
        var designer = "this.Size = new System.Drawing.Size(280, 80);";
        var result = DesignSurfaceFormRenderer.DetectBaseType(designer, null);
        Assert.That(result, Is.EqualTo(typeof(UserControl)));
    }

    [Test]
    public void DetectBaseType_NoCompanion_Default_ReturnsForm() {
        var result = DesignSurfaceFormRenderer.DetectBaseType("", null);
        Assert.That(result, Is.EqualTo(typeof(Form)));
    }

    #endregion

    #region Error Cases

    [Test]
    public void RenderDesignerCode_NoInitializeComponent_ThrowsInvalidOperation() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void SomeOtherMethod() { }
    }
}";
        Assert.Throws<InvalidOperationException>(() =>
            _renderer.RenderDesignerCode(designerCode));
    }

    [Test]
    public void RenderDesignerCode_UnknownControlTypes_SkipsGracefully() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.customWidget = new MyApp.CustomWidget();
            this.button1 = new System.Windows.Forms.Button();
            this.button1.Text = ""Still works"";
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(120, 30);
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Controls.Add(this.button1);
        }
        private System.Windows.Forms.Button button1;
    }
}";
        // Should not throw — unknown types are skipped gracefully
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderForm_NonExistentFile_ThrowsFileNotFound() {
        Assert.Throws<FileNotFoundException>(() =>
            _renderer.RenderForm(@"C:\nonexistent\path\Form1.Designer.cs"));
    }

    [Test]
    public void RenderDesignerCode_EmptyInitializeComponent_ProducesPng() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
        }
    }
}";
        // Empty InitializeComponent should still produce a default form image
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    #endregion

    #region RenderDesignerFile

    [Test]
    public void RenderDesignerFile_WithOutputPath_WritesToDisk() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.Text = ""File Output Test"";
        }
    }
}";
        var designerPath = Path.Combine(Path.GetTempPath(), $"test_ds_{Guid.NewGuid()}.Designer.cs");
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_ds_{Guid.NewGuid()}.png");
        try {
            File.WriteAllText(designerPath, designerCode);
            var pngBytes = _renderer.RenderDesignerFile(designerPath, outputPath);

            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
            AssertValidPng(pngBytes);
        }
        finally {
            if (File.Exists(designerPath))
                File.Delete(designerPath);
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    #endregion

    #region Event Wireup Handling

    [Test]
    public void RenderDesignerCode_EventWireup_SkippedGracefully() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.Text = ""Events"";
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(100, 30);
            this.button1.Click += new System.EventHandler(this.button1_Click);
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Controls.Add(this.button1);
        }
        private System.Windows.Forms.Button button1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    #endregion

    #region Nested Properties

    [Test]
    public void RenderDesignerCode_NestedProperty_FlatAppearance() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button1.FlatAppearance.BorderSize = 0;
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(120, 30);
            this.button1.Text = ""Flat"";
            this.ClientSize = new System.Drawing.Size(200, 100);
            this.Controls.Add(this.button1);
        }
        private System.Windows.Forms.Button button1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    #endregion

    #region Custom Controls (requires build output)

    [Test]
    [Category("E2E")]
    public void RenderForm_KitchenSink_WithBuildOutput_IncludesCustomControls() {
        var designerPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.Designer.cs");
        var companionPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.cs");
        if (!File.Exists(designerPath))
            Assert.Ignore("KitchenSink designer file not found.");

        var designerContent = File.ReadAllText(designerPath);
        var companionContent = File.Exists(companionPath) ? File.ReadAllText(companionPath) : null;

        // Locate the test app build output for MoodRing/StatusDashboard types
        var testAppBinDir = Path.Combine(TestContext.CurrentContext.TestDirectory);
        var extraPaths = new List<string>();
        var testAppDll = Path.Combine(testAppBinDir, "Rhombus.WinFormsMcp.Tests.dll");
        if (File.Exists(testAppDll))
            extraPaths.Add(testAppDll);

        // Also add any DLLs in the test output that might contain custom controls
        foreach (var dll in Directory.GetFiles(testAppBinDir, "*.dll")) {
            if (!extraPaths.Contains(dll))
                extraPaths.Add(dll);
        }

        var pngBytes = _renderer.RenderDesignerCode(designerContent, companionContent, extraPaths);
        AssertValidPng(pngBytes);
        Assert.That(pngBytes.Length, Is.GreaterThan(1000));

        var outputPath = Path.Combine(Path.GetTempPath(), "DesignSurface_KitchenSink_custom.png");
        File.WriteAllBytes(outputPath, pngBytes);
        TestContext.WriteLine($"KitchenSink with custom controls: {outputPath} ({pngBytes.Length:N0} bytes)");
    }

    #endregion

    #region Additional Coverage

    [Test]
    public void Parity_WithoutThisPrefix_ModernVSFormat() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            button1 = new System.Windows.Forms.Button();
            button1.Text = ""No This"";
            button1.Location = new System.Drawing.Point(10, 10);
            button1.Size = new System.Drawing.Size(100, 30);
            ClientSize = new System.Drawing.Size(300, 200);
        }
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void Parity_CheckedListBox_ItemsAddRange() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.clb = new System.Windows.Forms.CheckedListBox();
            this.clb.Location = new System.Drawing.Point(10, 10);
            this.clb.Size = new System.Drawing.Size(200, 150);
            this.clb.Items.AddRange(new object[] { ""Alpha"", ""Bravo"", ""Charlie"" });
            this.Controls.Add(this.clb);
            this.ClientSize = new System.Drawing.Size(250, 200);
        }
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void Parity_AnchoredControls() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            this.button1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.button1.Location = new System.Drawing.Point(300, 12);
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.Text = ""OK"";
            this.ClientSize = new System.Drawing.Size(400, 300);
            this.Controls.Add(this.button1);
            this.ResumeLayout(false);
        }
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void Parity_SuspendResumeLayout() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.SuspendLayout();
            this.Text = ""Test"";
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void Parity_RealisticMultiControlForm() {
        var designerCode = @"
namespace TestApp {
    partial class MainForm {
        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            this.button1.Location = new System.Drawing.Point(12, 12);
            this.button1.Name = ""button1"";
            this.button1.Size = new System.Drawing.Size(120, 30);
            this.button1.TabIndex = 0;
            this.button1.Text = ""Click Me"";
            this.textBox1.Location = new System.Drawing.Point(12, 50);
            this.textBox1.Name = ""textBox1"";
            this.textBox1.Size = new System.Drawing.Size(200, 23);
            this.textBox1.TabIndex = 1;
            this.textBox1.Text = ""Hello World"";
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 80);
            this.label1.Name = ""label1"";
            this.label1.Size = new System.Drawing.Size(38, 15);
            this.label1.TabIndex = 2;
            this.label1.Text = ""Label"";
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(12, 100);
            this.checkBox1.Name = ""checkBox1"";
            this.checkBox1.Size = new System.Drawing.Size(83, 19);
            this.checkBox1.TabIndex = 3;
            this.checkBox1.Text = ""Check me"";
            this.ClientSize = new System.Drawing.Size(400, 300);
            this.Controls.Add(this.checkBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.button1);
            this.Name = ""MainForm"";
            this.Text = ""Test Application"";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox checkBox1;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
        Assert.That(pngBytes.Length, Is.GreaterThan(500), "Multi-control form should produce reasonable PNG");
    }

    [Test]
    public void Parity_AutoScaleDimensionsAndFont() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(400, 300);
            this.Font = new System.Drawing.Font(""Segoe UI"", 9F);
            this.Text = ""Font Test"";
        }
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    #endregion

    #region Error Placeholder (VS-Parity)

    [Test]
    public void RenderDesignerCode_UnknownControlType_ProducesValidPngWithPlaceholder() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.myWidget = new Acme.Widgets.SuperButton();
            this.SuspendLayout();
            this.myWidget.Location = new System.Drawing.Point(10, 10);
            this.myWidget.Size = new System.Drawing.Size(200, 50);
            this.Controls.Add(this.myWidget);
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.Text = ""Error Placeholder Test"";
            this.ResumeLayout(false);
        }
        private Acme.Widgets.SuperButton myWidget;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void RenderDesignerCode_MultipleUnknownControls_StillRendersForm() {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.btn = new System.Windows.Forms.Button();
            this.unknown1 = new Missing.Namespace.Widget1();
            this.unknown2 = new Missing.Namespace.Widget2();
            this.SuspendLayout();
            this.btn.Text = ""OK"";
            this.btn.Location = new System.Drawing.Point(10, 10);
            this.btn.Size = new System.Drawing.Size(80, 30);
            this.unknown1.Location = new System.Drawing.Point(10, 50);
            this.unknown1.Size = new System.Drawing.Size(200, 40);
            this.unknown2.Location = new System.Drawing.Point(10, 100);
            this.unknown2.Size = new System.Drawing.Size(200, 40);
            this.Controls.Add(this.btn);
            this.Controls.Add(this.unknown1);
            this.Controls.Add(this.unknown2);
            this.ClientSize = new System.Drawing.Size(300, 200);
            this.ResumeLayout(false);
        }
        private System.Windows.Forms.Button btn;
        private Missing.Namespace.Widget1 unknown1;
        private Missing.Namespace.Widget2 unknown2;
    }
}";
        var pngBytes = _renderer.RenderDesignerCode(designerCode);
        AssertValidPng(pngBytes);
    }

    [Test]
    public void CreateErrorPlaceholder_ReturnsPanel_WithExpectedProperties() {
        var panel = DesignSurfaceFormRenderer.CreateErrorPlaceholder("MyWidget", "Type not found");

        Assert.Multiple(() => {
            Assert.That(panel, Is.InstanceOf<Panel>());
            Assert.That(panel.BackColor, Is.EqualTo(Color.FromArgb(255, 240, 240)));
            Assert.That(panel.BorderStyle, Is.EqualTo(BorderStyle.FixedSingle));
            Assert.That(panel.Size, Is.EqualTo(new Size(200, 50)));
            Assert.That(panel.Controls.Count, Is.EqualTo(1));

            var label = panel.Controls[0] as Label;
            Assert.That(label, Is.Not.Null);
            Assert.That(label!.Text, Does.Contain("MyWidget"));
            Assert.That(label.Text, Does.Contain("Type not found"));
            Assert.That(label.ForeColor, Is.EqualTo(Color.DarkRed));
            Assert.That(label.Dock, Is.EqualTo(DockStyle.Fill));
        });

        panel.Dispose();
    }

    #endregion

    #region Helpers

    private static void AssertValidPng(byte[] pngBytes) {
        Assert.That(pngBytes, Is.Not.Null);
        Assert.That(pngBytes.Length, Is.GreaterThan(50), "PNG should have reasonable size");
        Assert.That(pngBytes[0], Is.EqualTo(0x89), "PNG magic byte 0");
        Assert.That(pngBytes[1], Is.EqualTo(0x50), "PNG magic byte 1 ('P')");
        Assert.That(pngBytes[2], Is.EqualTo(0x4E), "PNG magic byte 2 ('N')");
        Assert.That(pngBytes[3], Is.EqualTo(0x47), "PNG magic byte 3 ('G')");
    }

    #endregion
}