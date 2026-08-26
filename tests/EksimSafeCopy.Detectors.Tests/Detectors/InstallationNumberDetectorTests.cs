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

public class InstallationNumberDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    public InstallationNumberDetectorTests()
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
    public void Detect_TesisatNoWithLabel_ReturnsDetection()
    {
        var document = CreateDocument("Tesisat No: 1234567890");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TesisatNo && d.Value == "1234567890");
    }

    [Fact]
    public void Detect_AboneNoWithLabel_ReturnsDetection()
    {
        var document = CreateDocument("Abone Numarası: 1234567890");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TesisatNo && d.Value == "1234567890");
    }

    [Fact]
    public void Detect_SayacNoWithLabel_ReturnsDetection()
    {
        var document = CreateDocument("Sayaç Numarası: 1234567890");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TesisatNo && d.Value == "1234567890");
    }

    [Fact]
    public void Detect_NumberWithoutLabel_NotDetected()
    {
        var document = CreateDocument("Number: 1234567890");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.TesisatNo);
    }

    [Fact]
    public void Detect_ShortNumber_NotDetected()
    {
        var document = CreateDocument("Tesisat No: 12345");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.TesisatNo);
    }

    [Fact]
    public void Detect_AlphanumericNumber_ReturnsDetection()
    {
        var document = CreateDocument("Tesisat No: ABC123456");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.TesisatNo && d.Value == "ABC123456");
    }
}