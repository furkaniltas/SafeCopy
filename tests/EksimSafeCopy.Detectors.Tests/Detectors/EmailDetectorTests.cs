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

public class EmailDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    public EmailDetectorTests()
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
    public void Detect_SimpleEmail_ReturnsDetection()
    {
        var document = CreateDocument("E-posta: ahmet.yilmaz@example.com");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Email && d.Value == "ahmet.yilmaz@example.com");
    }

    [Fact]
    public void Detect_EmailWithSubdomain_ReturnsDetection()
    {
        var document = CreateDocument("Email: user@mail.company.com.tr");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Email && d.Value == "user@mail.company.com.tr");
    }

    [Fact]
    public void Detect_EmailWithPlusAddressing_ReturnsDetection()
    {
        var document = CreateDocument("E-mail: user+tag@example.com");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Email && d.Value == "user+tag@example.com");
    }

    [Fact]
    public void Detect_EmailWithUppercase_ReturnsDetection()
    {
        var document = CreateDocument("E-POSTA: AHMET@EXAMPLE.COM");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Email && d.Value == "AHMET@EXAMPLE.COM");
    }

    [Fact]
    public void Detect_InvalidEmailMissingAt_NotDetected()
    {
        var document = CreateDocument("Invalid: ahmetyilmaz.example.com");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Email);
    }

    [Fact]
    public void Detect_InvalidEmailNoDomain_NotDetected()
    {
        var document = CreateDocument("Invalid: ahmet@");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Email);
    }

    [Fact]
    public void Detect_InvalidEmailNoTLD_NotDetected()
    {
        var document = CreateDocument("Invalid: ahmet@example");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Email);
    }

    [Fact]
    public void Detect_InvalidEmailDoubleDot_NotDetected()
    {
        var document = CreateDocument("Invalid: ahmet..yilmaz@example.com");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Email);
    }

    [Fact]
    public void Detect_InvalidEmailStartsWithDot_NotDetected()
    {
        var document = CreateDocument("Invalid: .ahmet@example.com");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Email);
    }

    [Fact]
    public void Detect_MultipleEmails_AllDetected()
    {
        var document = CreateDocument("Emails: ahmet@example.com, ayse@company.org");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Email).ToList();
        detections.Should().HaveCount(2);
        detections.Select(d => d.Value).Should().Contain("ahmet@example.com", "ayse@company.org");
    }

    [Fact]
    public void Detect_EmailWithLabel_HighConfidence()
    {
        var document = CreateDocument("E-posta: ahmet@example.com");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.Email);
        detection.ConfidenceLevel.Should().Be(ConfidenceLevel.Critical);
    }

    [Fact]
    public void Detect_EmailDomainProperty_Populated()
    {
        var document = CreateDocument("E-posta: ahmet.yilmaz@company.com.tr");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.Email);
        detection.Properties.Should().ContainKey("domain");
        detection.Properties["domain"].Should().Be("company.com.tr");
    }
}