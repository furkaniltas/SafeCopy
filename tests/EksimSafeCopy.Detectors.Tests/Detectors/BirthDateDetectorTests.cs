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

public class BirthDateDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    public BirthDateDetectorTests()
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
    public void Detect_DotFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "15.06.1990");
    }

    [Fact]
    public void Detect_SlashFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("Doğum T.: 15/06/1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "15/06/1990");
    }

    [Fact]
    public void Detect_DashFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("Doğum: 15-06-1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "15-06-1990");
    }

    [Fact]
    public void Detect_ISOFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("DTarih: 1990-06-15");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "1990-06-15");
    }

    [Fact]
    public void Detect_DateWithoutBirthLabel_NotDetected()
    {
        var document = CreateDocument("Bugünün tarihi: 15.06.2024");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_FutureDate_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.2030");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_InvalidDay_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 32.01.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_InvalidMonth_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 15.13.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_YearBefore1900_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.1899");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_MultipleBirthDates_AllDetected()
    {
        var document = CreateDocument("Anne: 15.06.1960 Baba: 20.03.1955");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Date).ToList();
        detections.Should().HaveCount(2);
        detections.Select(d => d.Value).Should().Contain("15.06.1960", "20.03.1955");
    }

    [Fact]
    public void Detect_BirthDateFormatProperty_Populated()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.Date);
        detection.Properties.Should().ContainKey("format");
        detection.Properties["format"].Should().BeOneOf("dot", "slash", "dash", "iso");
    }
}