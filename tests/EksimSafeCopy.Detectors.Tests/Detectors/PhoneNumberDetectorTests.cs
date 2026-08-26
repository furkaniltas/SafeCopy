using System.IO;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Detectors;
using EksimSafeCopy.Detectors.Detection.Pipeline;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.Detectors.Tests.Detectors;

public class PhoneNumberDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    public PhoneNumberDetectorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();

        var provider = services.BuildServiceProvider();
        _detectionEngine = provider.GetRequiredService<IDetectionEngine>();
    }

    private Document CreateDocument(string text)
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
            Name = "test.txt",
            Format = DF.Txt,
            Pages = new[] { page },
            Metadata = new DocumentMetadata()
        };
    }

    [Fact]
    public void Detect_MobileWithZeroPrefix_ReturnsDetection()
    {
        var document = CreateDocument("Telefon: 0532 123 45 67");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Phone).ToList();
        detections.Should().ContainSingle(d => d.Value == "5321234567");
        detections.First().Properties["phone_type"].Should().Be("mobile");
    }

    [Fact]
    public void Detect_MobileWithPlus90_ReturnsDetection()
    {
        var document = CreateDocument("GSM: +90 532 123 45 67");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Phone).ToList();
        detections.Should().ContainSingle(d => d.Value == "5321234567");
    }

    [Fact]
    public void Detect_MobileWith0090_ReturnsDetection()
    {
        var document = CreateDocument("Cep: 00905321234567");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Phone).ToList();
        detections.Should().ContainSingle(d => d.Value == "5321234567");
    }

    [Fact]
    public void Detect_MobileWithParenthesis_ReturnsDetection()
    {
        var document = CreateDocument("Tel: (0532) 123 45 67");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Phone).ToList();
        detections.Should().ContainSingle(d => d.Value == "5321234567");
    }

    [Fact]
    public void Detect_Landline_ReturnsDetection()
    {
        var document = CreateDocument("Telefon: 0212 123 45 67");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Phone).ToList();
        detections.Should().ContainSingle(d => d.Value == "2121234567");
        detections.First().Properties["phone_type"].Should().Be("landline");
    }

    [Fact]
    public void Detect_ShortNumber_NotDetected()
    {
        var document = CreateDocument("Number: 532 123 45");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Phone);
    }

    [Fact]
    public void Detect_LongNumber_NotDetected()
    {
        var document = CreateDocument("Number: 0532 123 45 67 89");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Phone);
    }

    [Fact]
    public void Detect_MultiplePhones_AllDetected()
    {
        var document = CreateDocument("Cep: 0532 123 45 67 Ev: 0212 123 45 67");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Phone).ToList();
        detections.Should().HaveCount(2);
        detections.Select(d => d.Value).Should().Contain("5321234567", "2121234567");
    }

    [Fact]
    public void Detect_PhoneWithLabel_HighConfidence()
    {
        var document = CreateDocument("Telefon: 0532 123 45 67");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.Phone);
        detection.ConfidenceLevel.Should().Be(ConfidenceLevel.Critical);
    }
}