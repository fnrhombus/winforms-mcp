using System.Drawing;
using System.Windows.Forms;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class FormRendererTests
{
    private FormRenderer _renderer = null!;

    [SetUp]
    public void Setup()
    {
        _renderer = new FormRenderer();
    }

    #region Type Resolution

    [Test]
    [TestCase("System.Windows.Forms.Button", typeof(Button))]
    [TestCase("System.Windows.Forms.TextBox", typeof(TextBox))]
    [TestCase("System.Windows.Forms.Label", typeof(Label))]
    [TestCase("System.Windows.Forms.ComboBox", typeof(ComboBox))]
    [TestCase("System.Windows.Forms.CheckBox", typeof(CheckBox))]
    [TestCase("System.Windows.Forms.Panel", typeof(Panel))]
    [TestCase("System.Windows.Forms.GroupBox", typeof(GroupBox))]
    [TestCase("System.Windows.Forms.ListBox", typeof(ListBox))]
    public void ResolveType_StandardWinFormsTypes_ReturnsCorrectType(string typeName, Type expected)
    {
        var result = _renderer.ResolveType(typeName);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase("Button", typeof(Button))]
    [TestCase("TextBox", typeof(TextBox))]
    [TestCase("Point", typeof(Point))]
    [TestCase("Size", typeof(Size))]
    public void ResolveType_ShortNames_ReturnsCorrectType(string typeName, Type expected)
    {
        var result = _renderer.ResolveType(typeName);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveType_UnknownType_ReturnsNull()
    {
        var result = _renderer.ResolveType("MyApp.CustomWidget");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ResolveType_CachesResults()
    {
        var first = _renderer.ResolveType("System.Windows.Forms.Button");
        var second = _renderer.ResolveType("System.Windows.Forms.Button");
        Assert.That(first, Is.SameAs(second));
    }

    #endregion

    #region Expression Evaluation

    [Test]
    public void EvaluateExpression_StringLiteral_ReturnsString()
    {
        var expr = ParseExpression("\"Hello World\"");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo("Hello World"));
    }

    [Test]
    public void EvaluateExpression_IntLiteral_ReturnsInt()
    {
        var expr = ParseExpression("42");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void EvaluateExpression_FloatLiteral_ReturnsFloat()
    {
        var expr = ParseExpression("8.25F");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(8.25f));
    }

    [Test]
    public void EvaluateExpression_BoolLiteral_ReturnsBool()
    {
        var expr = ParseExpression("true");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(true));
    }

    [Test]
    public void EvaluateExpression_ObjectCreation_Point()
    {
        var expr = ParseExpression("new System.Drawing.Point(10, 20)");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(new Point(10, 20)));
    }

    [Test]
    public void EvaluateExpression_ObjectCreation_Size()
    {
        var expr = ParseExpression("new System.Drawing.Size(400, 300)");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(new Size(400, 300)));
    }

    [Test]
    public void EvaluateExpression_ObjectCreation_Font()
    {
        var expr = ParseExpression("new System.Drawing.Font(\"Segoe UI\", 9F)");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.InstanceOf<Font>());
        var font = (Font)result!;
        Assert.That(font.Name, Is.EqualTo("Segoe UI"));
        Assert.That(font.Size, Is.EqualTo(9f));
    }

    [Test]
    public void EvaluateExpression_StaticMember_ColorRed()
    {
        var expr = ParseExpression("System.Drawing.Color.Red");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(Color.Red));
    }

    [Test]
    public void EvaluateExpression_StaticMethod_ColorFromArgb()
    {
        var expr = ParseExpression("System.Drawing.Color.FromArgb(255, 0, 0)");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(Color.FromArgb(255, 0, 0)));
    }

    [Test]
    public void EvaluateExpression_EnumFlags_BitwiseOr()
    {
        var expr = ParseExpression("System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(AnchorStyles.Top | AnchorStyles.Left));
    }

    [Test]
    public void EvaluateExpression_EnumFlags_MultipleOr()
    {
        var expr = ParseExpression("System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
    }

    [Test]
    public void EvaluateExpression_Cast_ReturnsInnerValue()
    {
        var expr = ParseExpression("((int)(284))");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(284));
    }

    [Test]
    public void EvaluateExpression_ArrayCreation_ObjectArray()
    {
        var expr = ParseExpression("new object[] { \"A\", \"B\", \"C\" }");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.InstanceOf<object[]>());
        var arr = (object[])result!;
        Assert.That(arr, Has.Length.EqualTo(3));
        Assert.That(arr[0], Is.EqualTo("A"));
        Assert.That(arr[1], Is.EqualTo("B"));
        Assert.That(arr[2], Is.EqualTo("C"));
    }

    [Test]
    public void EvaluateExpression_Negation_ReturnsNegated()
    {
        var expr = ParseExpression("-1");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void EvaluateExpression_UnknownExpression_ReturnsSkipped()
    {
        // An expression type we don't handle: typeof(...)
        var expr = ParseExpression("typeof(string)");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.InstanceOf<EvaluationSkipped>());
    }

    [Test]
    public void EvaluateExpression_ResourceReference_ReturnsSkipped()
    {
        var expr = ParseExpression("resources.GetObject(\"icon\")");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.InstanceOf<EvaluationSkipped>());
    }

    [Test]
    public void EvaluateExpression_Padding()
    {
        var expr = ParseExpression("new System.Windows.Forms.Padding(3)");
        var result = _renderer.EvaluateExpression(expr);
        Assert.That(result, Is.EqualTo(new Padding(3)));
    }

    #endregion

    #region Statement Execution

    [Test]
    public void ExecuteStatement_ObjectCreation_StoresInFields()
    {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.Text = ""Click Me"";
            this.button1.Location = new System.Drawing.Point(10, 20);
            this.button1.Size = new System.Drawing.Size(100, 30);
            this.Controls.Add(this.button1);
        }
    }
}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_statement_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void ExecuteStatement_EventWireup_Skipped()
    {
        // Event wireups should not throw
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.Click += new System.EventHandler(this.button1_Click);
            this.Controls.Add(this.button1);
        }
    }
}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_event_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void ExecuteStatement_SuspendResumeLayout_DoesNotThrow()
    {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.SuspendLayout();
            this.Text = ""Test"";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_layout_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    #endregion

    #region End-to-End

    [Test]
    public void RenderDesignerCode_RealisticForm_ProducesValidPng()
    {
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
            //
            // button1
            //
            this.button1.Location = new System.Drawing.Point(12, 12);
            this.button1.Name = ""button1"";
            this.button1.Size = new System.Drawing.Size(120, 30);
            this.button1.TabIndex = 0;
            this.button1.Text = ""Click Me"";
            //
            // textBox1
            //
            this.textBox1.Location = new System.Drawing.Point(12, 50);
            this.textBox1.Name = ""textBox1"";
            this.textBox1.Size = new System.Drawing.Size(200, 23);
            this.textBox1.TabIndex = 1;
            this.textBox1.Text = ""Hello World"";
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 80);
            this.label1.Name = ""label1"";
            this.label1.Size = new System.Drawing.Size(38, 15);
            this.label1.TabIndex = 2;
            this.label1.Text = ""Label"";
            //
            // checkBox1
            //
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(12, 100);
            this.checkBox1.Name = ""checkBox1"";
            this.checkBox1.Size = new System.Drawing.Size(83, 19);
            this.checkBox1.TabIndex = 3;
            this.checkBox1.Text = ""Check me"";
            //
            // MainForm
            //
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

        var outputPath = Path.Combine(Path.GetTempPath(), $"test_e2e_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);

            Assert.That(File.Exists(outputPath), Is.True);
            var fileInfo = new FileInfo(outputPath);
            Assert.That(fileInfo.Length, Is.GreaterThan(0));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void RenderDesignerCode_NoInitializeComponent_ThrowsInvalidOperation()
    {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void SomeOtherMethod() { }
    }
}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_noinit_{Guid.NewGuid()}.png");
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                _renderer.RenderDesignerCode(designerCode, outputPath));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void RenderDesignerCode_UnknownControlTypes_SkipsGracefully()
    {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.customWidget = new MyApp.CustomWidget();
            this.button1 = new System.Windows.Forms.Button();
            this.button1.Text = ""Still works"";
            this.Controls.Add(this.button1);
        }
    }
}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_unknown_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void RenderDesignerFile_ValidFile_ProducesPng()
    {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.Text = ""File Test"";
            this.ClientSize = new System.Drawing.Size(300, 200);
        }
    }
}";
        var designerPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.Designer.cs");
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_file_{Guid.NewGuid()}.png");
        try
        {
            File.WriteAllText(designerPath, designerCode);
            _renderer.RenderDesignerFile(designerPath, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
        }
        finally
        {
            if (File.Exists(designerPath)) File.Delete(designerPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void RenderDesignerCode_FormWithFont()
    {
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
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_font_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void RenderDesignerCode_FormWithAnchoredControls()
    {
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
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_anchor_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void RenderDesignerCode_WithoutThisPrefix_StillWorks()
    {
        // Modern format without this. prefix
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            button1 = new System.Windows.Forms.Button();
            button1.Text = ""No This"";
            button1.Location = new System.Drawing.Point(10, 10);
            button1.Size = new System.Drawing.Size(100, 30);
        }
    }
}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_nothis_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Test]
    public void RenderDesignerCode_WithColorProperties()
    {
        var designerCode = @"
namespace Test {
    partial class TestForm {
        private void InitializeComponent() {
            this.button1 = new System.Windows.Forms.Button();
            this.button1.BackColor = System.Drawing.Color.Red;
            this.button1.ForeColor = System.Drawing.Color.FromArgb(255, 255, 255);
            this.button1.Text = ""Colored"";
            this.button1.Location = new System.Drawing.Point(10, 10);
            this.button1.Size = new System.Drawing.Size(100, 30);
            this.Controls.Add(this.button1);
        }
    }
}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_color_{Guid.NewGuid()}.png");
        try
        {
            _renderer.RenderDesignerCode(designerCode, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    #endregion

    #region KitchenSink

    [Test]
    public void RenderDesignerCode_CheckedListBox_ShowsItems()
    {
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
        var outputPath = Path.Combine(Path.GetTempPath(), "test_clb_roslyn.png");
        _renderer.RenderDesignerCode(designerCode, outputPath);
        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
    }

    [Test]
    public void RenderDesignerFile_KitchenSink_ProducesPng()
    {
        var designerPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "KitchenSink", "KitchenSinkForm.Designer.cs");
        if (!File.Exists(designerPath))
            Assert.Ignore("KitchenSink designer file not found.");

        var outputPath = Path.Combine(Path.GetTempPath(), "KitchenSink_roslyn.png");
        try
        {
            _renderer.RenderDesignerFile(designerPath, outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
            TestContext.WriteLine($"Rendered: {outputPath}");
        }
        finally
        {
            // Keep for visual review - don't delete
        }
    }

    #endregion

    #region Helpers

    private static ExpressionSyntax ParseExpression(string expressionText)
    {
        var code = $"class C {{ void M() {{ var x = {expressionText}; }} }}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();
        return root.DescendantNodes()
            .OfType<EqualsValueClauseSyntax>()
            .First()
            .Value;
    }

    #endregion
}
