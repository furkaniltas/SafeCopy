using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.Renderer.Redaction;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Xunit;

namespace SafeCopy.Renderer.Tests.Redaction;

public class RedactionPlannerTests
{
    private readonly IRedactionStrategy _strategy;
    private readonly IRedactionPlanner _planner;

    public RedactionPlannerTests()
    {
        _strategy = new DefaultRedactionStrategy();
        _planner = new RedactionPlanner(_strategy);
    }

    private static Document CreateDocument(string name, DF format, params DocumentPage[] pages)
    {
        return new Document(name, format, pages);
    }

    private static DocumentPage CreatePage(int pageNumber, string text, int width = 800, int height = 600)
    {
        return new DocumentPage(width, height, pageNumber, 72, 72)
        {
            Text = text,
            PageNumber = pageNumber
        };
    }

    [Fact]
    public void CreatePlan_DetectionToOperation_MapsCorrectly()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz 11111111111"));
        var detection = new Detection
        {
            Type = DetectionType.FullName,
            Value = "Ahmet Yılmaz",
            TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" },
            PageNumber = 1,
            Confidence = 0.95,
            Location = new BoundingBox(10, 10, 100, 20)
        };

        var options = new RenderOptions();
        var result = _planner.CreatePlan(doc, new[] { detection }, options);

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
        var op = result.Value.Operations[0];
        op.DetectionId.Should().Be(detection.Id);
        op.DetectionType.Should().Be(DetectionType.FullName);
        op.TextSpan!.Text.Should().Be("Ahmet Yılmaz");
        op.PageNumber.Should().Be(1);
        op.Confidence.Should().Be(0.95);
        op.State.Should().Be(RedactionOperationState.Pending);
        op.BoundingBox.Should().Be(detection.Location);
        op.ReplacementText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreatePlan_DuplicateDetections_CreatesSeparateOperations()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz Ahmet Yılmaz"));
        var d1 = new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9 };
        var d2 = new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 13, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9 };

        var result = _planner.CreatePlan(doc, new[] { d1, d2 }, new RenderOptions());

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(2);
        result.Value.Operations[0].TextSpan!.StartIndex.Should().Be(0);
        result.Value.Operations[1].TextSpan!.StartIndex.Should().Be(13);
    }

    [Fact]
    public void CreatePlan_OverlappingDetections_BothIncludedAndOrdered()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz 11111111111"));
        var d1 = new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9 };
        var d2 = new Detection { Type = DetectionType.FullName, Value = "Yılmaz 111", TextSpan = new TextSpan { StartIndex = 6, Length = 10, Text = "Yılmaz 111" }, PageNumber = 1, Confidence = 0.9 };

        var result = _planner.CreatePlan(doc, new[] { d2, d1 }, new RenderOptions());

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(2);
        // Ordered by StartIndex ascending
        result.Value.Operations[0].TextSpan!.StartIndex.Should().Be(0);
        result.Value.Operations[1].TextSpan!.StartIndex.Should().Be(6);
        d1.TextSpan.Overlaps(d2.TextSpan).Should().BeTrue();
    }

    [Fact]
    public void CreatePlan_DeterministicOrdering_SameInputProducesSameOrder()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "abc def ghi"));
        var detections = new[]
        {
            new Detection { Type = DetectionType.Email, Value = "ghi", TextSpan = new TextSpan { StartIndex = 8, Length = 3, Text = "ghi" }, PageNumber = 2, Confidence = 0.9 },
            new Detection { Type = DetectionType.Phone, Value = "abc", TextSpan = new TextSpan { StartIndex = 0, Length = 3, Text = "abc" }, PageNumber = 1, Confidence = 0.9 },
            new Detection { Type = DetectionType.Iban, Value = "def", TextSpan = new TextSpan { StartIndex = 4, Length = 3, Text = "def" }, PageNumber = 1, Confidence = 0.9 },
        };

        var r1 = _planner.CreatePlan(doc, detections, new RenderOptions());
        var r2 = _planner.CreatePlan(doc, detections, new RenderOptions());

        r1.IsSuccess.Should().BeTrue();
        r2.IsSuccess.Should().BeTrue();
        var order1 = string.Join(",", r1.Value.Operations.Select(o => $"{o.PageNumber}:{o.TextSpan!.StartIndex}"));
        var order2 = string.Join(",", r2.Value.Operations.Select(o => $"{o.PageNumber}:{o.TextSpan!.StartIndex}"));
        order1.Should().Be(order2);
        // Expected: page 1 start 0, page 1 start 4, page 2 start 8
        r1.Value.Operations[0].PageNumber.Should().Be(1);
        r1.Value.Operations[0].TextSpan!.StartIndex.Should().Be(0);
        r1.Value.Operations[1].PageNumber.Should().Be(1);
        r1.Value.Operations[1].TextSpan!.StartIndex.Should().Be(4);
        r1.Value.Operations[2].PageNumber.Should().Be(2);
    }

    [Fact]
    public void CreatePlan_MultiplePages_OperationsGroupedByPage()
    {
        var doc = CreateDocument("test.docx", DF.Docx,
            CreatePage(1, "Page1 Ahmet"),
            CreatePage(2, "Page2 11111111111"),
            CreatePage(3, "Page3 test@example.com"));
        var detections = new[]
        {
            new Detection { Type = DetectionType.FullName, Value = "Ahmet", TextSpan = new TextSpan { StartIndex = 6, Length = 5, Text = "Ahmet" }, PageNumber = 1, Confidence = 0.9 },
            new Detection { Type = DetectionType.TcKimlikNo, Value = "11111111111", TextSpan = new TextSpan { StartIndex = 6, Length = 11, Text = "11111111111" }, PageNumber = 2, Confidence = 0.9 },
            new Detection { Type = DetectionType.Email, Value = "test@example.com", TextSpan = new TextSpan { StartIndex = 6, Length = 16, Text = "test@example.com" }, PageNumber = 3, Confidence = 0.9 },
        };

        var result = _planner.CreatePlan(doc, detections, new RenderOptions());

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(3);
        result.Value.Operations[0].PageNumber.Should().Be(1);
        result.Value.Operations[1].PageNumber.Should().Be(2);
        result.Value.Operations[2].PageNumber.Should().Be(3);
        result.Value.DocumentId.Should().Be(doc.Id);
        result.Value.Format.Should().Be(DF.Docx);
    }

    [Fact]
    public void CreatePlan_CoordinateMapping_PreservesBoundingBox()
    {
        var doc = CreateDocument("test.png", DF.Png, CreatePage(1, "image text"));
        var bbox = new BoundingBox(10, 20, 100, 50, 800, 600);
        var detection = new Detection
        {
            Type = DetectionType.TcKimlikNo,
            Value = "11111111111",
            TextSpan = new TextSpan { StartIndex = 0, Length = 11, Text = "11111111111", BoundingBox = bbox },
            Location = bbox,
            PageNumber = 1,
            Confidence = 0.95
        };

        var result = _planner.CreatePlan(doc, new[] { detection }, new RenderOptions());

        result.IsSuccess.Should().BeTrue();
        var op = result.Value.Operations[0];
        op.BoundingBox.X.Should().Be(10);
        op.BoundingBox.Y.Should().Be(20);
        op.BoundingBox.Width.Should().Be(100);
        op.BoundingBox.Height.Should().Be(50);
        op.TextSpan!.BoundingBox.Should().Be(bbox);
    }

    [Fact]
    public void CreatePlan_ConfidenceThreshold_FiltersLowConfidence()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet 11111111111"));
        var high = new Detection { Type = DetectionType.FullName, Value = "Ahmet", TextSpan = new TextSpan { StartIndex = 0, Length = 5, Text = "Ahmet" }, Confidence = 0.9, PageNumber = 1 };
        var low = new Detection { Type = DetectionType.TcKimlikNo, Value = "11111111111", TextSpan = new TextSpan { StartIndex = 6, Length = 11, Text = "11111111111" }, Confidence = 0.3, PageNumber = 1 };

        var options = new RenderOptions { ConfidenceThreshold = 0.5 };
        var result = _planner.CreatePlan(doc, new[] { high, low }, options);

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations[0].DetectionType.Should().Be(DetectionType.FullName);
    }

    [Fact]
    public void CreatePlan_StateHandling_SkipsDeselectedAndFalsePositive()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Mehmet Ayşe"));
        var selected = new Detection { Type = DetectionType.FullName, Value = "Ahmet", TextSpan = new TextSpan { StartIndex = 0, Length = 5, Text = "Ahmet" }, Confidence = 0.9, PageNumber = 1, State = DetectionState.Selected };
        var deselected = new Detection { Type = DetectionType.FullName, Value = "Mehmet", TextSpan = new TextSpan { StartIndex = 6, Length = 6, Text = "Mehmet" }, Confidence = 0.9, PageNumber = 1, State = DetectionState.Deselected };
        var falsePositive = new Detection { Type = DetectionType.FullName, Value = "Ayşe", TextSpan = new TextSpan { StartIndex = 13, Length = 4, Text = "Ayşe" }, Confidence = 0.9, PageNumber = 1, State = DetectionState.FalsePositive };
        var detected = new Detection { Type = DetectionType.FullName, Value = "Ahmet2", TextSpan = new TextSpan { StartIndex = 0, Length = 6, Text = "Ahmet2" }, Confidence = 0.9, PageNumber = 1, State = DetectionState.Detected };

        var result = _planner.CreatePlan(doc, new[] { selected, deselected, falsePositive, detected }, new RenderOptions());

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(2);
        result.Value.Operations.Select(o => o.DetectionId).Should().Contain(selected.Id);
        result.Value.Operations.Select(o => o.DetectionId).Should().Contain(detected.Id);
        result.Value.Operations.Select(o => o.DetectionId).Should().NotContain(deselected.Id);
        result.Value.Operations.Select(o => o.DetectionId).Should().NotContain(falsePositive.Id);
    }

    [Fact]
    public void CreatePlan_MaskedState_IsIncluded()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet"));
        var masked = new Detection { Type = DetectionType.FullName, Value = "Ahmet", TextSpan = new TextSpan { StartIndex = 0, Length = 5, Text = "Ahmet" }, Confidence = 0.9, PageNumber = 1, State = DetectionState.Masked };

        var result = _planner.CreatePlan(doc, new[] { masked }, new RenderOptions());

        // Masked is not Deselected/FalsePositive, so should be included (planner only filters those two)
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
    }

    [Fact]
    public void CreatePlan_EmptyDetections_ReturnsEmptyPlan()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "clean"));
        var result = _planner.CreatePlan(doc, Array.Empty<Detection>(), new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().BeEmpty();
    }

    [Fact]
    public void CreatePlan_NullStrategy_Throws()
    {
        Action act = () => new RedactionPlanner(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CreatePlanAsync_ReturnsSameAsSync()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz"));
        var detection = new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, Confidence = 0.9, PageNumber = 1 };
        var options = new RenderOptions();

        var sync = _planner.CreatePlan(doc, new[] { detection }, options);
        var async = await _planner.CreatePlanAsync(doc, new[] { detection }, options);

        sync.IsSuccess.Should().BeTrue();
        async.IsSuccess.Should().BeTrue();
        async.Value.Operations.Count.Should().Be(sync.Value.Operations.Count);
        async.Value.DocumentId.Should().Be(sync.Value.DocumentId);
    }

    [Fact]
    public void Possible_Partial_Deselected_NoOperation()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz"));
        var d = new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Deselected };
        var options = new RenderOptions { Mode = MaskingMode.PartialMask };
        var result = _planner.CreatePlan(doc, new[] { d }, options);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Possible_Partial_Selected_ProducesPossiblePiiPlaceholder()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz"));
        var d = new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected };
        var options = new RenderOptions { Mode = MaskingMode.PartialMask };
        var result = _planner.CreatePlan(doc, new[] { d }, options);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations[0].ReplacementText.Should().Be("[OLASI_KİŞİSEL_VERİ]");
        result.Value.Operations[0].DetectionType.Should().Be(DetectionType.PossiblePersonalData);
    }

    [Fact]
    public void Possible_Full_Deselected_NoOperation()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz"));
        var d = new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Deselected };
        var options = new RenderOptions { Mode = MaskingMode.FullRedaction, UseTypePlaceholder = true };
        var result = _planner.CreatePlan(doc, new[] { d }, options);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Possible_Full_Selected_ProducesPossiblePiiPlaceholder()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz"));
        var d = new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected };
        var options = new RenderOptions { Mode = MaskingMode.FullRedaction, UseTypePlaceholder = true };
        var result = _planner.CreatePlan(doc, new[] { d }, options);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations[0].ReplacementText.Should().Be("[OLASI_KİŞİSEL_VERİ]");
    }

    [Fact]
    public void DefinitiveAndPossible_SameSpan_BothSelected_PrioritizesDefinitive()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "12132133"));
        var definitive = new Detection { Type = DetectionType.TesisatNo, Value = "12132133", TextSpan = new TextSpan { StartIndex = 0, Length = 8, Text = "12132133" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected };
        var possible = new Detection { Type = DetectionType.PossiblePersonalData, Value = "12132133", TextSpan = new TextSpan { StartIndex = 0, Length = 8, Text = "12132133" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected };
        var options = new RenderOptions { Mode = MaskingMode.PartialMask };
        var result = _planner.CreatePlan(doc, new[] { definitive, possible }, options);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations[0].DetectionType.Should().Be(DetectionType.TesisatNo);
        result.Value.Operations[0].ReplacementText.Should().NotBe("[OLASI_KİŞİSEL_VERİ]");
    }

    [Fact]
    public void DefinitiveDeselected_PossibleSelected_SameSpan_ProducesPossible()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "12132133"));
        var definitive = new Detection { Type = DetectionType.TesisatNo, Value = "12132133", TextSpan = new TextSpan { StartIndex = 0, Length = 8, Text = "12132133" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Deselected };
        var possible = new Detection { Type = DetectionType.PossiblePersonalData, Value = "12132133", TextSpan = new TextSpan { StartIndex = 0, Length = 8, Text = "12132133" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected };
        var options = new RenderOptions { Mode = MaskingMode.PartialMask };
        var result = _planner.CreatePlan(doc, new[] { definitive, possible }, options);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations[0].DetectionType.Should().Be(DetectionType.PossiblePersonalData);
        result.Value.Operations[0].ReplacementText.Should().Be("[OLASI_KİŞİSEL_VERİ]");
    }

    [Fact]
    public void RealisticScenario_Partial_WithPossibleSelected_ProducesCorrectPlaceholders()
    {
        var doc = CreateDocument("test.txt", DF.Txt, CreatePage(1, "Ahmet Yılmaz 0555 123 45 67 12132133 ahmet.yilmaz@example.invalid 14.03.1990 12345678901"));
        var detections = new[]
        {
            new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected },
            new Detection { Type = DetectionType.Phone, Value = "0555 123 45 67", TextSpan = new TextSpan { StartIndex = 13, Length = 14, Text = "0555 123 45 67" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected },
            new Detection { Type = DetectionType.TesisatNo, Value = "12132133", TextSpan = new TextSpan { StartIndex = 28, Length = 8, Text = "12132133" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected },
            new Detection { Type = DetectionType.Email, Value = "ahmet.yilmaz@example.invalid", TextSpan = new TextSpan { StartIndex = 37, Length = 27, Text = "ahmet.yilmaz@example.invalid" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected },
            new Detection { Type = DetectionType.Date, Value = "14.03.1990", TextSpan = new TextSpan { StartIndex = 65, Length = 10, Text = "14.03.1990" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected },
            new Detection { Type = DetectionType.TcKimlikNo, Value = "12345678901", TextSpan = new TextSpan { StartIndex = 76, Length = 11, Text = "12345678901" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected },
            new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 90, Length = 12, Text = "Ahmet Yılmaz" }, PageNumber = 1, Confidence = 0.9, State = DetectionState.Selected }
        };
        var options = new RenderOptions { Mode = MaskingMode.PartialMask };
        // Use planner with all strategies to correctly resolve PartialMask
        var planner = new RedactionPlanner(new DefaultRedactionStrategy(), new IRedactionStrategy[] { new DefaultRedactionStrategy(), new PartialMaskStrategy(), new FullRedactionStrategy(), new PlaceholderStrategy() });
        var result = planner.CreatePlan(doc, detections, options);
        result.IsSuccess.Should().BeTrue();
        // 6 definitive + 1 possible distinct span = 7 operations
        result.Value.Operations.Should().HaveCount(7);
        result.Value.Operations.Count(o => o.DetectionType == DetectionType.PossiblePersonalData).Should().Be(1);
        result.Value.Operations.First(o => o.DetectionType == DetectionType.PossiblePersonalData).ReplacementText.Should().Be("[OLASI_KİŞİSEL_VERİ]");
        // Definitive partial outputs unchanged
        result.Value.Operations.First(o => o.DetectionType == DetectionType.TcKimlikNo).ReplacementText.Should().Be("*******8901");
        result.Value.Operations.First(o => o.DetectionType == DetectionType.Phone).ReplacementText.Should().Contain("0555");
        result.Value.Operations.First(o => o.DetectionType == DetectionType.TesisatNo).ReplacementText.Should().Be("****2133");
    }
}

