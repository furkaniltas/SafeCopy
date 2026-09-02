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

public class PersonNameDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    public PersonNameDetectorTests()
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
    public void Detect_SimpleTurkishName_ReturnsDetection()
    {
        var document = CreateDocument("Ad Soyad: Ahmet Yılmaz");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Ahmet Yılmaz");
    }

    [Fact]
    public void Detect_ThreeWordName_ReturnsDetection()
    {
        var document = CreateDocument("Adı: Ahmet Mehmet Yılmaz");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Ahmet Mehmet Yılmaz");
    }

    [Fact]
    public void Detect_NameWithTurkishCharacters_ReturnsDetection()
    {
        var document = CreateDocument("İsim: Ahmet Çelik");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Ahmet Çelik");
    }

    [Fact]
    public void Detect_UppercaseName_ReturnsDetection()
    {
        var document = CreateDocument("MÜŞTERİ: AHMET YILMAZ");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "AHMET YILMAZ");
    }

    [Fact]
    public void Detect_SingleWord_NotDetected()
    {
        var document = CreateDocument("İsim: Ahmet");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName);
    }

    [Fact]
    public void Detect_CityName_NotDetected()
    {
        var document = CreateDocument("Adres: İstanbul");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName);
    }

    [Fact]
    public void Detect_CompanyName_NotDetected()
    {
        var document = CreateDocument("Firma: Elektrik Dağıtım A.Ş.");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName);
    }

    [Fact]
    public void Detect_DepartmentName_NotDetected()
    {
        var document = CreateDocument("Birim: Bilgi İşlem Müdürlüğü");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName);
    }

    [Fact]
    public void Detect_NameWithLabel_HigherConfidence()
    {
        var document = CreateDocument("Ad Soyad: Ahmet Yılmaz");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.FullName);
        detection.Confidence.Should().BeGreaterThan(0.7);
    }

    [Fact]
    public void Detect_NameWithNegativeContext_LowerConfidence()
    {
        var document = CreateDocument("Şirket: Ahmet Yılmaz Ltd.");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.FullName).ToList();
        detections.Should().BeEmpty();
    }

    [Fact]
    public void Detect_MultipleNames_AllDetected()
    {
        var document = CreateDocument("Müşteri: Ahmet Yılmaz Temsilci: Ayşe Demir");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.FullName).ToList();
        detections.Should().HaveCount(2);
        detections.Select(d => d.Value).Should().Contain("Ahmet Yılmaz", "Ayşe Demir");
    }

    [Fact]
    public void Detect_InstitutionName_DiyarbakirIcraDairesi_NotDetected()
    {
        var document = CreateDocument("DİYARBAKIR İCRA DAİRESİ");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName && d.Value == "DİYARBAKIR İCRA DAİRESİ");
    }

    [Fact]
    public void Detect_LegalDocumentHeader_NeEsasTalepEvraki_NotDetected()
    {
        var document = CreateDocument("NE ESAS TALEP EVRAKI");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName && d.Value == "NE ESAS TALEP EVRAKI");
    }

    [Fact]
    public void Detect_LegalPhrase_TakibinKesinlestirilmesini_NotDetected()
    {
        var document = CreateDocument("Takibin Kesinleştirilmesini");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName && d.Value == "Takibin Kesinleştirilmesini");
    }

    [Fact]
    public void Detect_RealPersonName_SabriGoclu_Detected()
    {
        var document = CreateDocument("SABRİ GÖÇLÜ");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "SABRİ GÖÇLÜ");
    }

    [Fact]
    public void Detect_Prefix_Sahip_DilaraYucel_Stripped()
    {
        var document = CreateDocument("Sahip Dilara Yücel");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Dilara Yücel");
        result.Value.Should().NotContain(d => d.Type == DetectionType.FullName && d.Value == "Sahip Dilara Yücel");
    }

    [Fact]
    public void Detect_Prefix_Alici_DorukGAltun_Stripped()
    {
        var document = CreateDocument("Alıcı Doruk G. Altun");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        var det = result.Value.Where(d => d.Type == DetectionType.FullName).ToList();
        det.Should().ContainSingle(d => d.Value == "Doruk G. Altun");
    }

    [Fact]
    public void Detect_Prefix_Danisan_SavasSinanYildirim_Stripped()
    {
        var document = CreateDocument("Danışan Savaş Sinan Yıldırım");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Savaş Sinan Yıldırım");
    }

    [Fact]
    public void Detect_MiddleInitial_LeventB_Yildirim()
    {
        var document = CreateDocument("Levent B. Yıldırım");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Levent B. Yıldırım");
    }

    [Fact]
    public void Detect_MiddleInitial_VeliV_Bozkurt()
    {
        var document = CreateDocument("Veli V. Bozkurt");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Veli V. Bozkurt");
    }

    [Fact]
    public void Detect_MiddleInitial_CemE_Sezer()
    {
        var document = CreateDocument("Cem E. Sezer");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Cem E. Sezer");
    }

    [Fact]
    public void Detect_MiddleInitial_YagmurP_Sakir_TurkishChars()
    {
        var document = CreateDocument("Yağmur P. Şakır");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Yağmur P. Şakır");
    }

    [Fact]
    public void Detect_MiddleInitial_DorukG_Altun_TurkishChars()
    {
        var document = CreateDocument("Doruk G. Altun");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Doruk G. Altun");
    }

    [Fact]
    public void Detect_TitleOnly_DanisanUzm_NotDetected()
    {
        var document = CreateDocument("Danışan Uzm");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.FullName).Should().BeEmpty();
    }

    [Fact]
    public void Detect_TitleOnly_TarafAv_NotDetected()
    {
        var document = CreateDocument("Taraf Av.");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.FullName).Should().BeEmpty();
    }

    [Fact]
    public void Detect_TitleOnly_HastaDyt_NotDetected()
    {
        var document = CreateDocument("Hasta Dyt.");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.FullName).Should().BeEmpty();
    }

    [Fact]
    public void Detect_AddressComponent_IcKapi_NotDetected()
    {
        var document = CreateDocument("İç Kapı 74Q");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.FullName && d.Value == "İç Kapı").Should().BeEmpty();
    }
}