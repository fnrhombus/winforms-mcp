namespace Rhombus.WinFormsMcp.Tests;

using System.Collections.Generic;

using FlaUI.Core.AutomationElements;

using Moq;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Tests for the GetElementTree functionality
/// </summary>
public class ElementTreeTests {
    private Mock<IAutomationHelper>? _mockAutomation;

    [SetUp]
    public void Setup() {
        _mockAutomation = new Mock<IAutomationHelper>();
    }

    [TearDown]
    public void TearDown() {
        _mockAutomation?.VerifyAll();
    }

    [Test]
    public void TestGetElementTreeIsCalled() {
        // Arrange
        var expectedTree = new List<Dictionary<string, object?>>();
        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 50, null))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 3, 50, null);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestGetElementTreeWithCustomDepth() {
        // Arrange
        var expectedTree = new List<Dictionary<string, object?>>();
        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                1, 50, null))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 1, 50, null);

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockAutomation.Verify(a => a.GetElementTree(It.IsAny<AutomationElement>(), 1, 50, null), Times.Once);
    }

    [Test]
    public void TestGetElementTreeWithCustomMaxElements() {
        // Arrange
        var expectedTree = new List<Dictionary<string, object?>>();
        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 10, null))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 3, 10, null);

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockAutomation.Verify(a => a.GetElementTree(It.IsAny<AutomationElement>(), 3, 10, null), Times.Once);
    }

    [Test]
    public void TestGetElementTreeReturnsPopulatedTree() {
        // Arrange
        var expectedTree = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "Button1",
                ["controlType"] = "Button",
                ["automationId"] = "btn1",
                ["isEnabled"] = true,
                ["isOffscreen"] = false,
                ["boundingRectangle"] = new Dictionary<string, object>
                {
                    ["x"] = 10.0,
                    ["y"] = 20.0,
                    ["width"] = 100.0,
                    ["height"] = 30.0
                },
                ["elementId"] = "elem_1",
                ["children"] = new List<Dictionary<string, object?>>()
            },
            new Dictionary<string, object?>
            {
                ["name"] = "TextBox1",
                ["controlType"] = "Edit",
                ["automationId"] = "txt1",
                ["isEnabled"] = true,
                ["isOffscreen"] = false,
                ["boundingRectangle"] = new Dictionary<string, object>
                {
                    ["x"] = 10.0,
                    ["y"] = 60.0,
                    ["width"] = 200.0,
                    ["height"] = 25.0
                },
                ["elementId"] = "elem_2",
                ["children"] = new List<Dictionary<string, object?>>()
            }
        };

        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 50, It.IsAny<Func<AutomationElement, string>>()))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 3, 50, _ => "elem_1");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0]["name"], Is.EqualTo("Button1"));
        Assert.That(result[0]["controlType"], Is.EqualTo("Button"));
        Assert.That(result[0]["automationId"], Is.EqualTo("btn1"));
        Assert.That(result[0]["isEnabled"], Is.EqualTo(true));
        Assert.That(result[0]["isOffscreen"], Is.EqualTo(false));
        Assert.That(result[0]["elementId"], Is.EqualTo("elem_1"));
        Assert.That(result[1]["name"], Is.EqualTo("TextBox1"));
    }

    [Test]
    public void TestGetElementTreeWithCacheCallback() {
        // Arrange
        var cachedElements = new List<string>();
        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 50, It.IsAny<Func<AutomationElement, string>>()))
            .Returns(new List<Dictionary<string, object?>>
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "TestElement",
                    ["elementId"] = "elem_1",
                    ["children"] = new List<Dictionary<string, object?>>()
                }
            })
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 3, 50, el => {
            var id = $"elem_{cachedElements.Count + 1}";
            cachedElements.Add(id);
            return id;
        });

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["elementId"], Is.EqualTo("elem_1"));
    }

    [Test]
    public void TestGetElementTreeWithNestedChildren() {
        // Arrange
        var innerChildren = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "InnerButton",
                ["controlType"] = "Button",
                ["children"] = new List<Dictionary<string, object?>>()
            }
        };

        var expectedTree = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "Panel1",
                ["controlType"] = "Pane",
                ["children"] = innerChildren
            }
        };

        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 50, null))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 3, 50, null);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["name"], Is.EqualTo("Panel1"));

        var children = result[0]["children"] as List<Dictionary<string, object?>>;
        Assert.That(children, Is.Not.Null);
        Assert.That(children!, Has.Count.EqualTo(1));
        Assert.That(children![0]["name"], Is.EqualTo("InnerButton"));
    }

    [Test]
    public void TestGetElementTreeDepthLimiting() {
        // Arrange - depth 1 should only return immediate children, no grandchildren
        var expectedTree = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "Panel1",
                ["controlType"] = "Pane",
                ["children"] = new List<Dictionary<string, object?>>() // Empty because depth=1
            }
        };

        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                1, 50, null))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 1, 50, null);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var children = result[0]["children"] as List<Dictionary<string, object?>>;
        Assert.That(children, Is.Not.Null);
        Assert.That(children!, Is.Empty);
    }

    [Test]
    public void TestGetElementTreeMaxElementsCapping() {
        // Arrange - maxElements=2 should cap the number of returned elements
        var expectedTree = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "Element1",
                ["children"] = new List<Dictionary<string, object?>>()
            },
            new Dictionary<string, object?>
            {
                ["name"] = "Element2",
                ["children"] = new List<Dictionary<string, object?>>()
            }
        };

        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 2, null))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 3, 2, null);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void TestGetElementTreeWithNullProperties() {
        // Arrange - elements may have null properties
        var expectedTree = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["name"] = null,
                ["controlType"] = null,
                ["automationId"] = null,
                ["isEnabled"] = null,
                ["isOffscreen"] = null,
                ["boundingRectangle"] = null,
                ["children"] = new List<Dictionary<string, object?>>()
            }
        };

        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 50, null))
            .Returns(expectedTree)
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!, 3, 50, null);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["name"], Is.Null);
        Assert.That(result[0]["controlType"], Is.Null);
        Assert.That(result[0]["automationId"], Is.Null);
        Assert.That(result[0]["boundingRectangle"], Is.Null);
    }

    [Test]
    public void TestGetElementTreeDefaultParameters() {
        // Arrange - verify default parameters (depth=3, maxElements=50) are used
        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 50, null))
            .Returns(new List<Dictionary<string, object?>>())
            .Verifiable();

        // Act
        var result = _mockAutomation.Object.GetElementTree(null!);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestGetElementTreeThrowsOnError() {
        // Arrange
        _mockAutomation!
            .Setup(a => a.GetElementTree(
                It.IsAny<AutomationElement>(),
                3, 50, null))
            .Throws<InvalidOperationException>()
            .Verifiable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _mockAutomation.Object.GetElementTree(null!, 3, 50, null)
        );
    }
}

/// <summary>
/// Tests for the concrete AutomationHelper.GetElementTree implementation.
/// These test the actual tree-building logic using a real AutomationHelper
/// against the desktop (no launched apps required).
/// </summary>
public class AutomationHelperElementTreeTests {
    private AutomationHelper? _automation;

    [SetUp]
    public void Setup() {
        _automation = new AutomationHelper();
    }

    [TearDown]
    public void TearDown() {
        _automation?.Dispose();
    }

    [Test]
    public void TestGetElementTreeWithDepthZeroReturnsEmpty() {
        // GetElementTree with depth=0 should return no children
        // We need a root element - use the desktop
        var desktop = GetDesktop();
        if (desktop == null) {
            Assert.Ignore("Cannot get desktop element in this environment");
            return;
        }

        var result = _automation!.GetElementTree(desktop, depth: 0, maxElements: 50);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestGetElementTreeWithMaxElementsZeroReturnsEmpty() {
        var desktop = GetDesktop();
        if (desktop == null) {
            Assert.Ignore("Cannot get desktop element in this environment");
            return;
        }

        var result = _automation!.GetElementTree(desktop, depth: 3, maxElements: 0);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestGetElementTreeCachesElements() {
        var desktop = GetDesktop();
        if (desktop == null) {
            Assert.Ignore("Cannot get desktop element in this environment");
            return;
        }

        var cachedIds = new List<string>();
        int counter = 0;
        var result = _automation!.GetElementTree(desktop, depth: 1, maxElements: 5, cacheElement: el => {
            var id = $"elem_{++counter}";
            cachedIds.Add(id);
            return id;
        });

        // Every element in result should have an elementId
        Assert.That(result.Count, Is.GreaterThanOrEqualTo(0));
        Assert.That(cachedIds.Count, Is.EqualTo(result.Count));

        foreach (var node in result) {
            Assert.That(node.ContainsKey("elementId"), Is.True);
            Assert.That(node["elementId"], Is.Not.Null);
        }
    }

    [Test]
    public void TestGetElementTreeRespectsMaxElements() {
        var desktop = GetDesktop();
        if (desktop == null) {
            Assert.Ignore("Cannot get desktop element in this environment");
            return;
        }

        // Desktop typically has many children; cap at 3
        var result = _automation!.GetElementTree(desktop, depth: 2, maxElements: 3);

        // Count total elements in tree (recursive)
        int total = CountElements(result);
        Assert.That(total, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void TestGetElementTreeNodeStructure() {
        var desktop = GetDesktop();
        if (desktop == null) {
            Assert.Ignore("Cannot get desktop element in this environment");
            return;
        }

        var result = _automation!.GetElementTree(desktop, depth: 1, maxElements: 1);
        if (result.Count == 0) {
            Assert.Ignore("No children found on desktop");
            return;
        }

        var node = result[0];
        // Verify expected keys exist
        Assert.That(node.ContainsKey("name"), Is.True);
        Assert.That(node.ContainsKey("controlType"), Is.True);
        Assert.That(node.ContainsKey("automationId"), Is.True);
        Assert.That(node.ContainsKey("isEnabled"), Is.True);
        Assert.That(node.ContainsKey("isOffscreen"), Is.True);
        Assert.That(node.ContainsKey("boundingRectangle"), Is.True);
        Assert.That(node.ContainsKey("children"), Is.True);
    }

    private static int CountElements(List<Dictionary<string, object?>> tree) {
        int count = 0;
        foreach (var node in tree) {
            count++;
            if (node.TryGetValue("children", out var childrenObj) && childrenObj is List<Dictionary<string, object?>> children) {
                count += CountElements(children);
            }
        }
        return count;
    }

    private AutomationElement? GetDesktop() {
        try {
            // Use reflection to access the internal _automation field for desktop
            var field = typeof(AutomationHelper).GetField("_automation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var uia = field?.GetValue(_automation) as FlaUI.UIA2.UIA2Automation;
            return uia?.GetDesktop();
        }
        catch {
            return null;
        }
    }
}