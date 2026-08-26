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

public class AddressDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    public AddressDetectorTests()
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
    public void Detect_FullAddress_ReturnsDetection()
    {
        var document = CreateDocument("Adres: Atatürk Mah. Cumhuriyet Cad. No: 12 D: 4 Kadıköy/İstanbul");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address && d.Value.Contains("Atatürk Mah"));
    }

    [Fact]
    public void Detect_SimpleAddress_ReturnsDetection()
    {
        var document = CreateDocument("Adres: Atatürk Mah. Cumhuriyet Cad. No: 12");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address && d.Value.Contains("Atatürk Mah"));
    }

    [Fact]
    public void Detect_AddressWithSokak_ReturnsDetection()
    {
        var document = CreateDocument("Adres: İnönü Mah. Atatürk Sok. No: 5");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address && d.Value.Contains("İnönü Mah"));
    }

    [Fact]
    public void Detect_AddressWithDaire_ReturnsDetection()
    {
        var document = CreateDocument("Adres: Çankaya Mah. Kızılay Cad. No: 10 Daire: 3");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address && d.Value.Contains("Çankaya Mah"));
    }

    [Fact]
    public void Detect_OnlyCity_NotDetected()
    {
        var document = CreateDocument("Şehir: İstanbul");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Address);
    }

    [Fact]
    public void Detect_CompanyAddress_NotDetected()
    {
        var document = CreateDocument("Şirket Adresi: Ankara Çankaya Mah. Atatürk Bulvarı No: 100");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Address).ToList();
        detections.Should().BeEmpty();
    }

    [Fact]
    public void Detect_AddressComponentCount_PropertyPopulated()
    {
        var document = CreateDocument("Adres: Atatürk Mah. Cumhuriyet Cad. No: 12 D: 4");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.Address);
        detection.Properties.Should().ContainKey("component_count");
        var count = (int)detection.Properties["component_count"];
        count.Should().BeGreaterOrEqualTo(3);
    }
}