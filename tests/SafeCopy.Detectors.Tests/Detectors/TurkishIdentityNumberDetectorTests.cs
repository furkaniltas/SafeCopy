using System.IO;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.Detectors.Detection.Detectors;
using SafeCopy.Detectors.Detection.Pipeline;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SafeCopy.Detectors.Tests.Detectors;

public class TurkishIdentityNumberDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    // Valid Turkish ID numbers with correct checksums
    private const string ValidTcKimlik = "10000000146";  // Known valid test number
    private const string ValidTcKimlik2 = "10000000146"; // Second instance of same valid number

    public TurkishIdentityNumberDetectorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();

        var provider = services.BuildServiceProvider();
        _detectionEngine = provider.GetRequiredService<IDetectionEngine>();
    }

    private Document CreateDocument(string text, string name = "test.txt")
    {
        var page = new DocumentPage
        {
            PageNumber = 1,
            Width = 800,
            Height = 600,
            DpiX = 96,
            DpiY = 96,
            Text = text,
            TextBlocks = new List<TextBlock>
            {
                new TextBlock
                {
                    Text = text,
                    Type = TextBlockType.Paragraph,
                    Direction = TextDirection.LeftToRight,
                    OrderIndex = 0,
                    PageNumber = 1
                }
            }.AsReadOnly()
        };

        return new Document
        {
            Name = name,
            Format = DF.Txt,
            Pages = new[] { page },
            Metadata = new DocumentMetadata()
        };
    }

    [Fact]
    public void Detect_ValidTcKimlik_ReturnsDetection()
    {
        var document = CreateDocument($"T.C. Kimlik No: {ValidTcKimlik}");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TcKimlikNo && d.Value == ValidTcKimlik);
    }

    [Fact]
    public void Detect_ValidTcKimlikWithSpaces_ReturnsDetection()
    {
        var spaced = string.Join(" ", ValidTcKimlik.Chunk(3).Select(c => new string(c)));
        var document = CreateDocument($"TC Kimlik: {spaced}");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TcKimlikNo && d.Value == ValidTcKimlik);
    }

    [Fact]
    public void Detect_ValidTcKimlikWithDashes_ReturnsDetection()
    {
        var dashed = string.Join("-", ValidTcKimlik.Chunk(3).Select(c => new string(c)));
        var document = CreateDocument($"Kimlik No: {dashed}");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TcKimlikNo && d.Value == ValidTcKimlik);
    }

    [Fact]
    public void Detect_InvalidTcKimlik_NotDetected()
    {
        var document = CreateDocument("Invalid: 11111111111");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.TcKimlikNo && d.Value == "11111111111");
    }

    [Fact]
    public void Detect_TcStartingWithZero_NotDetected()
    {
        var document = CreateDocument("TC: 01234567890");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.TcKimlikNo);
    }

    [Fact]
    public void Detect_TcWithWrongChecksum_NotDetected()
    {
        // Invalid checksum without strong label should not be detected; with "TC" label it would be detected via fallback
        var document = CreateDocument("No label here: 12345678902");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.TcKimlikNo);
    }

    [Fact]
    public void Detect_ShortNumber_NotDetected()
    {
        var document = CreateDocument("Number: 1234567890");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.TcKimlikNo);
    }

    [Fact]
    public void Detect_LongNumber_NotDetected()
    {
        var document = CreateDocument("Number: 123456789012");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.TcKimlikNo);
    }

    [Fact]
    public void Detect_TcInText_Detected()
    {
        var document = CreateDocument($"Ahmet Yılmaz TCKN {ValidTcKimlik} Ankara");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TcKimlikNo && d.Value == ValidTcKimlik);
    }

    [Fact]
    public void Detect_MultipleTcKimlik_AllDetected()
    {
        var document = CreateDocument($"First: {ValidTcKimlik} Second: {ValidTcKimlik2}");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.TcKimlikNo).ToList();
        detections.Should().HaveCount(2);
        detections.Select(d => d.Value).Should().Contain(ValidTcKimlik, ValidTcKimlik2);
    }

    [Fact]
    public void Detect_ConfidenceLevel_CriticalForHighConfidence()
    {
        var document = CreateDocument($"T.C. Kimlik No: {ValidTcKimlik}");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.TcKimlikNo);
        detection.ConfidenceLevel.Should().Be(ConfidenceLevel.Critical);
    }
}