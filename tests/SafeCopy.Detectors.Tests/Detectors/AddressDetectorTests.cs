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

    [Fact]
    public void Detect_Curated_AyazMah_ReturnsDetection()
    {
        var document = CreateDocument("Adres Ayaz Mah. Üzüm Sk. No:108 D:32, Bahçe Kapısı, İç Kapı 74Q, Buca/Manisa");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        var det = result.Value.Where(d => d.Type == DetectionType.Address).ToList();
        det.Should().NotBeEmpty();
        det[0].Value.Should().Contain("Ayaz Mah");
        det[0].TextSpan!.Text.Should().Contain("Ayaz Mah");
    }

    [Fact]
    public void Detect_Curated_ZeytinlikMah_ReturnsDetection()
    {
        var document = CreateDocument("Fatura adresi Zeytinlik Mah. Derya Sk. No:32 D:28, E Blok, İç Kapı 4EN, Tepebaşı/Antalya");
        var result = _detectionEngine.Detect(document);
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address && d.Value.Contains("Zeytinlik Mah"));
    }

    [Fact]
    public void Detect_Curated_KoruMah_ReturnsDetection()
    {
        var document = CreateDocument("Adres Koru Mah. Ahenk Sk. No:104 D:44, E Blok, İç Kapı JDN, Tepebaşı/Manisa");
        var result = _detectionEngine.Detect(document);
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address);
    }

    [Fact]
    public void Detect_Curated_BasakMh_Noisy_ReturnsDetection()
    {
        var document = CreateDocument("Fatura adresi Başak Mh.Akın S k.No:16 D=68, Arka Giriş, İç Kapı 4CP, Talas/Konya");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.Address).Should().NotBeEmpty();
        result.Value.First(d => d.Type == DetectionType.Address).Value.Should().Contain("Başak");
    }

    [Fact]
    public void Detect_Curated_ElvanMah_ReturnsDetection()
    {
        var document = CreateDocument("Adres Elvan Mah. Erguvan Sk. No:50 D:38, 2. Kat, İç Kapı VJ6, Pendik/Manisa");
        var result = _detectionEngine.Detect(document);
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address);
    }

    [Fact]
    public void Detect_Curated_CamliMah_TurkishChars_ReturnsDetection()
    {
        var document = CreateDocument("Bildirim adresi Çamlık Mah. Güvercin Sk. No:57 D:49, 3. Kat, İç Kapı HZZ, Kepez/Sakarya");
        var result = _detectionEngine.Detect(document);
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Address && d.Value.Contains("Çamlık Mah"));
    }

    [Fact]
    public void Detect_Curated_OptionalComponents_ReturnsDetection()
    {
        var document = CreateDocument("Adres Ayaz Mah. Üzüm Sk. No:108 D:32, Bahçe Kapısı, İç Kapı 74Q, Buca/Manisa");
        var result = _detectionEngine.Detect(document);
        var det = result.Value.First(d => d.Type == DetectionType.Address);
        det.Value.Should().Contain("Bahçe Kapısı");
        det.Value.Should().Contain("İç Kapı");
        det.Value.Should().Contain("Buca");
    }

    [Fact]
    public void Detect_Negative_OrdinarySentence_NotDetected()
    {
        var document = CreateDocument("Bugün hava güzel, No: 5 gibi bir şey değil, sadece normal bir cümle.");
        var result = _detectionEngine.Detect(document);
        result.Value.Where(d => d.Type == DetectionType.Address).Should().BeEmpty();
    }

    [Fact]
    public void Detect_Negative_PersonName_NotDetected()
    {
        var document = CreateDocument("Ahmet Yılmaz ile görüştüm.");
        var result = _detectionEngine.Detect(document);
        result.Value.Where(d => d.Type == DetectionType.Address).Should().BeEmpty();
    }

    [Fact]
    public void Detect_Negative_Phone_NotDetectedAsAddress()
    {
        var document = CreateDocument("Telefon: 0532 123 45 67");
        var result = _detectionEngine.Detect(document);
        result.Value.Where(d => d.Type == DetectionType.Address).Should().BeEmpty();
    }

    [Fact]
    public void Detect_Negative_AccountNumber_NotDetectedAsAddress()
    {
        var document = CreateDocument("Hesap: KRT-6545 7568 1468 7417");
        var result = _detectionEngine.Detect(document);
        result.Value.Where(d => d.Type == DetectionType.Address).Should().BeEmpty();
    }

    [Fact]
    public void Detect_Negative_ServerLog_NotDetected()
    {
        var document = CreateDocument("2026-05-17T04:20:12+03:00 api[2960]: action=booking");
        var result = _detectionEngine.Detect(document);
        result.Value.Where(d => d.Type == DetectionType.Address).Should().BeEmpty();
    }
}