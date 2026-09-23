using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.Renderer.Redaction;
using FluentAssertions;
using Xunit;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;

namespace SafeCopy.Renderer.Tests.Redaction;

public class IbanRedactionTests
{
    private static Document CreateDocument(string name, DF format, params DocumentPage[] pages) => new(name, format, pages);
    private static DocumentPage CreatePage(int pageNumber, string text, int width = 800, int height = 600) => new(width, height, pageNumber, 72, 72) { Text = text, PageNumber = pageNumber };

    private IRedactionPlanner CreatePlanner()
    {
        var strategies = new IRedactionStrategy[] { new DefaultRedactionStrategy(), new PartialMaskStrategy(), new FullRedactionStrategy(), new PlaceholderStrategy() };
        return new RedactionPlanner(new DefaultRedactionStrategy(), strategies);
    }

    [Fact]
    public void Iban_Partial_Spaced_MasksCorrectly()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(0, "TR33 0006 1005 1978 6457 8413 26"));
        var d = new Detection { Type = DetectionType.Iban, Value = "TR33 0006 1005 1978 6457 8413 26", TextSpan = new TextSpan { StartIndex = 0, Length = 29, Text = "TR33 0006 1005 1978 6457 8413 26" }, PageNumber = 0, Confidence = 0.95, State = DetectionState.Selected };
        var planner = CreatePlanner();
        var result = planner.CreatePlan(doc, new[] { d }, new RenderOptions { Mode = MaskingMode.PartialMask });
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations[0].ReplacementText.Should().Be("TR33 **** **** **** **** **** 26");
    }

    [Fact]
    public void Iban_Full_Placeholder()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(0, "TR33 0006 1005 1978 6457 8413 26"));
        var d = new Detection { Type = DetectionType.Iban, Value = "TR33 0006 1005 1978 6457 8413 26", TextSpan = new TextSpan { StartIndex = 0, Length = 29, Text = "TR33 0006 1005 1978 6457 8413 26" }, PageNumber = 0, Confidence = 0.95, State = DetectionState.Selected };
        var planner = CreatePlanner();
        var result = planner.CreatePlan(doc, new[] { d }, new RenderOptions { Mode = MaskingMode.FullRedaction, UseTypePlaceholder = true });
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations[0].ReplacementText.Should().Be("[IBAN]");
    }

    [Fact]
    public void Iban_Unspaced_Partial()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(0, "TR330006100519786457841326"));
        var d = new Detection { Type = DetectionType.Iban, Value = "TR330006100519786457841326", TextSpan = new TextSpan { StartIndex = 0, Length = 26, Text = "TR330006100519786457841326" }, PageNumber = 0, Confidence = 0.95, State = DetectionState.Selected };
        var planner = CreatePlanner();
        var result = planner.CreatePlan(doc, new[] { d }, new RenderOptions { Mode = MaskingMode.PartialMask });
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations[0].ReplacementText.Should().Be("TR33********************26");
    }

    [Fact]
    public void Iban_Deselected_NoOperation()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(0, "TR33 0006 1005 1978 6457 8413 26"));
        var d = new Detection { Type = DetectionType.Iban, Value = "TR33 0006 1005 1978 6457 8413 26", TextSpan = new TextSpan { StartIndex = 0, Length = 29, Text = "TR33 0006 1005 1978 6457 8413 26" }, PageNumber = 0, Confidence = 0.95, State = DetectionState.Deselected };
        var planner = CreatePlanner();
        var result = planner.CreatePlan(doc, new[] { d }, new RenderOptions { Mode = MaskingMode.PartialMask });
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().BeEmpty();
    }
}
