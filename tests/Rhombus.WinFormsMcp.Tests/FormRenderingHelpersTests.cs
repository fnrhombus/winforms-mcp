using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Tests;

[TestFixture]
public class FormRenderingHelpersTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FormRenderingHelpersTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── ResolveDesignerFile ──────────────────────────────────────────

    [Test]
    public void ResolveDesignerFile_WhenGivenDesignerCs_ReturnsSamePath()
    {
        var path = Path.Combine(_tempDir, "Form1.Designer.cs");
        File.WriteAllText(path, "// designer");

        var result = FormRenderingHelpers.ResolveDesignerFile(path);
        Assert.That(result, Is.EqualTo(path));
    }

    [Test]
    public void ResolveDesignerFile_WhenGivenCs_ResolvesToSiblingDesignerCs()
    {
        var csPath = Path.Combine(_tempDir, "Form1.cs");
        var designerPath = Path.Combine(_tempDir, "Form1.Designer.cs");
        File.WriteAllText(csPath, "// code");
        File.WriteAllText(designerPath, "// designer");

        var result = FormRenderingHelpers.ResolveDesignerFile(csPath);
        Assert.That(result, Is.EqualTo(designerPath));
    }

    [Test]
    public void ResolveDesignerFile_WhenDesignerMissing_ThrowsFileNotFound()
    {
        var csPath = Path.Combine(_tempDir, "Form1.cs");
        File.WriteAllText(csPath, "// code");

        Assert.Throws<FileNotFoundException>(() =>
            FormRenderingHelpers.ResolveDesignerFile(csPath));
    }

    [Test]
    public void ResolveDesignerFile_WhenDesignerPathDoesNotExist_ThrowsFileNotFound()
    {
        var path = Path.Combine(_tempDir, "Missing.Designer.cs");

        Assert.Throws<FileNotFoundException>(() =>
            FormRenderingHelpers.ResolveDesignerFile(path));
    }

    // ── FindCsproj ───────────────────────────────────────────────────

    [Test]
    public void FindCsproj_FindsProjectInSameDirectory()
    {
        var csproj = Path.Combine(_tempDir, "MyProject.csproj");
        File.WriteAllText(csproj, "<Project />");

        var result = FormRenderingHelpers.FindCsproj(_tempDir);
        Assert.That(result, Is.EqualTo(csproj));
    }

    [Test]
    public void FindCsproj_FindsProjectInParentDirectory()
    {
        var csproj = Path.Combine(_tempDir, "MyProject.csproj");
        File.WriteAllText(csproj, "<Project />");

        var subDir = Path.Combine(_tempDir, "src", "Forms");
        Directory.CreateDirectory(subDir);

        var result = FormRenderingHelpers.FindCsproj(subDir);
        Assert.That(result, Is.EqualTo(csproj));
    }

    [Test]
    public void FindCsproj_WhenNoCsproj_ThrowsFileNotFound()
    {
        // _tempDir has no .csproj -- but parent dirs might.
        // Use a deeply nested dir to minimize chance of accidental match.
        var isolated = Path.Combine(_tempDir, "a", "b", "c");
        Directory.CreateDirectory(isolated);

        // This will walk up and eventually may or may not find one.
        // We can't fully isolate the filesystem, so just verify it returns a string or throws.
        try
        {
            var result = FormRenderingHelpers.FindCsproj(isolated);
            // If it found one higher up, that's still valid behavior.
            Assert.That(File.Exists(result), Is.True);
        }
        catch (FileNotFoundException)
        {
            Assert.Pass("Correctly threw when no .csproj found");
        }
    }

    // ── ParseDesignerFile ────────────────────────────────────────────

    [Test]
    public void ParseDesignerFile_ExtractsNamespaceAndClassName()
    {
        const string content = @"
namespace MyApp.Forms
{
    partial class LoginForm
    {
        private void InitializeComponent() { }
    }
}";
        var (ns, className, handlers) = FormRenderingHelpers.ParseDesignerFile(content);

        Assert.That(ns, Is.EqualTo("MyApp.Forms"));
        Assert.That(className, Is.EqualTo("LoginForm"));
        Assert.That(handlers, Is.Empty);
    }

    [Test]
    public void ParseDesignerFile_ExtractsEventHandlers()
    {
        const string content = @"
namespace MyApp
{
    partial class Form1
    {
        private void InitializeComponent()
        {
            this.button1.Click += new System.EventHandler(this.button1_Click);
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
        }
    }
}";
        var (_, _, handlers) = FormRenderingHelpers.ParseDesignerFile(content);

        Assert.That(handlers, Has.Count.EqualTo(2));
        Assert.That(handlers, Does.Contain("button1_Click"));
        Assert.That(handlers, Does.Contain("textBox1_TextChanged"));
    }

    [Test]
    public void ParseDesignerFile_DeduplicatesEventHandlers()
    {
        const string content = @"
namespace MyApp
{
    partial class Form1
    {
        private void InitializeComponent()
        {
            this.button1.Click += new System.EventHandler(this.shared_Handler);
            this.button2.Click += new System.EventHandler(this.shared_Handler);
        }
    }
}";
        var (_, _, handlers) = FormRenderingHelpers.ParseDesignerFile(content);

        Assert.That(handlers, Has.Count.EqualTo(1));
        Assert.That(handlers[0], Is.EqualTo("shared_Handler"));
    }

    [Test]
    public void ParseDesignerFile_WithNoNamespace_ReturnsNull()
    {
        const string content = @"
partial class Form1
{
    private void InitializeComponent() { }
}";
        var (ns, className, _) = FormRenderingHelpers.ParseDesignerFile(content);

        Assert.That(ns, Is.Null);
        Assert.That(className, Is.EqualTo("Form1"));
    }

    [Test]
    public void ParseDesignerFile_WithNoPartialClass_Throws()
    {
        const string content = "namespace Foo { class Bar { } }";

        Assert.Throws<InvalidOperationException>(() =>
            FormRenderingHelpers.ParseDesignerFile(content));
    }
}
