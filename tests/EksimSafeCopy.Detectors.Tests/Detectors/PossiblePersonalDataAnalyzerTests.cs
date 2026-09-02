using System.Linq;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using EksimSafeCopy.Detectors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using DetectionModel = EksimSafeCopy.Core.Models.Detection;

namespace EksimSafeCopy.Detectors.Tests.Detectors;

public class PossiblePersonalDataAnalyzerTests
{
    private readonly IDetectionEngine _engine;

    public PossiblePersonalDataAnalyzerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();
        var sp = services.BuildServiceProvider();
        _engine = sp.GetRequiredService<IDetectionEngine>();
    }

    private Document Doc(string text)
    {
        var page = new DocumentPage
        {
            PageNumber = 1, Width = 800, Height = 600, DpiX = 96, DpiY = 96,
            Text = text,
            TextBlocks = new[] { new TextBlock { Text = text, Type = TextBlockType.Paragraph, Direction = TextDirection.LeftToRight, OrderIndex = 0, PageNumber = 1 } }.ToList().AsReadOnly()
        };
        return new Document { Name = "test.txt", Format = DF.Txt, Pages = new[] { page }, Metadata = new DocumentMetadata() };
    }

    private IReadOnlyList<DetectionModel> Possible(string text) => _engine.Detect(Doc(text)).Value.Where(d => d.Type == DetectionType.PossiblePersonalData).ToList();

    [Fact] public void Possible_BasvuruSahibi_AyseDemir()
    {
        var all = _engine.Detect(Doc("Başvuru Sahibi: Ayşe Demir")).Value;
        var ayat = all.Where(d => d.Value == "Ayşe Demir").ToList();
        ayat.Should().HaveCount(1);
        ayat.Single().Type.Should().BeOneOf(DetectionType.FullName, DetectionType.PossiblePersonalData);
        all.Where(d => d.Value == "Başvuru Sahibi").Should().BeEmpty();
        // duplicate invariant
        all.Where(d => d.Value == "Ayşe Demir").Select(d => d.TextSpan!.StartIndex).Distinct().Should().HaveCount(1);
    }
    [Fact] public void Possible_MusteriAdi_MehmetKaya()
    {
        var all = _engine.Detect(Doc("Müşteri Adı: Mehmet Kaya")).Value;
        var m = all.Where(d => d.Value == "Mehmet Kaya").ToList();
        m.Should().HaveCount(1);
        m.Single().Type.Should().BeOneOf(DetectionType.FullName, DetectionType.PossiblePersonalData);
        all.Where(d => d.Value == "Müşteri Adı").Should().BeEmpty();
    }
    [Fact] public void Possible_IlgiliKisi_AhmetYilmaz()
    {
        var all = _engine.Detect(Doc("İlgili Kişi: Ahmet Yılmaz")).Value;
        var a = all.Where(d => d.Value == "Ahmet Yılmaz").ToList();
        a.Should().HaveCount(1);
        a.Single().Type.Should().BeOneOf(DetectionType.FullName, DetectionType.PossiblePersonalData);
        all.Where(d => d.Value == "İlgili Kişi").Should().BeEmpty();
    }
    [Fact] public void Possible_Diyarbakir_NotDetected() => Possible("DİYARBAKIR İCRA DAİRESİ").Should().BeEmpty();
    [Fact] public void Possible_NeEsas_NotDetected() => Possible("NE ESAS TALEP EVRAKI").Should().BeEmpty();
    [Fact] public void Possible_Takibin_NotDetected() => Possible("Takibin Kesinleştirilmesini").Should().BeEmpty();
    [Fact] public void Possible_AnkaraBolge_NotDetected() => Possible("Ankara Bölge Müdürlüğü").Should().BeEmpty();
    [Fact] public void Possible_MusteriBasvuruFormu_NotDetected() => Possible("Müşteri Başvuru Formu").Should().BeEmpty();
    [Fact] public void Possible_DogumTarihiLabel_NotDetected()
    {
        var all = _engine.Detect(Doc("Doğum Tarihi")).Value;
        all.Where(d => d.Type == DetectionType.FullName && d.Value == "Doğum Tarihi").Should().BeEmpty();
        Possible("Doğum Tarihi").Should().BeEmpty();
    }
    [Fact] public void Possible_KimlikNoLabel_NotDetected()
    {
        var all = _engine.Detect(Doc("Kimlik No")).Value;
        all.Where(d => d.Type == DetectionType.FullName && d.Value == "Kimlik No").Should().BeEmpty();
        Possible("Kimlik No").Should().BeEmpty();
    }
    [Fact] public void Possible_AdSoyadLabel_NotDetected()
    {
        var all = _engine.Detect(Doc("Ad Soyad")).Value;
        all.Where(d => d.Type == DetectionType.FullName && d.Value == "Ad Soyad").Should().BeEmpty();
        Possible("Ad Soyad").Should().BeEmpty();
    }
    [Fact] public void Possible_SabriGoclu_FullNameNotPossible() {
        var all = _engine.Detect(Doc("SABRİ GÖÇLÜ")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "SABRİ GÖÇLÜ");
        all.Should().NotContain(d => d.Type == DetectionType.PossiblePersonalData);
    }
    [Fact] public void Possible_FurkanIltas_FullNameNotPossible() {
        var all = _engine.Detect(Doc("Furkan İltaş")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Furkan İltaş");
        all.Should().NotContain(d => d.Type == DetectionType.PossiblePersonalData);
    }
    [Fact] public void Possible_TcProximity_AyseDemir() {
        var all = _engine.Detect(Doc("TC Kimlik No: 12345678901\nBaşvuru Sahibi: Ayşe Demir")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.TcKimlikNo);
        var ayat = all.Where(d => d.Value == "Ayşe Demir").ToList();
        ayat.Should().HaveCount(1);
        ayat.Single().Type.Should().BeOneOf(DetectionType.FullName, DetectionType.PossiblePersonalData);
    }
    [Fact] public void Possible_DateLabel_NotPossibleButDateIsDefinite() {
        var all = _engine.Detect(Doc("Doğum Tarihi: 12.04.1988")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "12.04.1988");
        all.Where(d => d.Type == DetectionType.PossiblePersonalData).Should().BeEmpty();
        all.Where(d => d.Type == DetectionType.FullName && d.Value == "Doğum Tarihi").Should().BeEmpty();
    }
    [Fact] public void Possible_Duplicate_FullNameNotDuplicated() {
        var all = _engine.Detect(Doc("SABRI GOCLU")).Value;
        var full = all.Where(d => d.Type == DetectionType.FullName).ToList();
        var poss = all.Where(d => d.Type == DetectionType.PossiblePersonalData).ToList();
        full.Should().HaveCount(1);
        poss.Should().BeEmpty();
        foreach(var f in full) foreach(var p in poss)
            (f.TextSpan!.StartIndex < p.TextSpan!.EndIndex && p.TextSpan!.StartIndex < f.TextSpan!.EndIndex).Should().BeFalse();
    }
    [Fact] public void Possible_IsolatedName_NotDetected() {
        Possible("Ayşe Demir").Should().BeEmpty();
    }
    [Fact] public void Possible_FarProximity_NotDetected() {
        var filler = new string('x', 300);
        Possible($"TC Kimlik No: 12345678901\n{filler}\nAhmet Yılmaz").Where(d => d.Value == "Ahmet Yılmaz").Should().BeEmpty();
    }

    // Required regression tests for precision hardening
    [Fact] public void Possible_BasvuruSahibi_Alone_NotDetected()
    {
        var all = _engine.Detect(Doc("Başvuru Sahibi")).Value;
        all.Where(d => d.Value == "Başvuru Sahibi").Should().BeEmpty();
        Possible("Başvuru Sahibi").Should().BeEmpty();
    }
    [Fact] public void Possible_CankayaMahallesi_NotDetected() => Possible("Çankaya Mahallesi").Should().BeEmpty();
    [Fact] public void Possible_IslemBilgileri_NotDetected() => Possible("İşlem Bilgileri").Should().BeEmpty();
    [Fact] public void Possible_TcPlusIslemBilgileri_NoPossible()
    {
        var all = _engine.Detect(Doc("TC Kimlik No: 12345678901\n\nİşlem Bilgileri")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.TcKimlikNo);
        Possible("TC Kimlik No: 12345678901\n\nİşlem Bilgileri").Where(d => d.Value == "İşlem Bilgileri").Should().BeEmpty();
    }
    [Fact] public void Possible_TesisatPlusCankaya_NoPossible()
    {
        var all = _engine.Detect(Doc("Tesisat No: 12132133\n\nÇankaya Mahallesi")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.TesisatNo);
        Possible("Tesisat No: 12132133\n\nÇankaya Mahallesi").Where(d => d.Value == "Çankaya Mahallesi").Should().BeEmpty();
    }
    [Fact] public void Possible_AdSoyadAhmetYilmaz_NoDuplicate()
    {
        var all = _engine.Detect(Doc("Ad Soyad: Ahmet Yılmaz")).Value;
        var ahmet = all.Where(d => d.Value == "Ahmet Yılmaz").ToList();
        ahmet.Should().HaveCount(1);
        ahmet.Single().Type.Should().BeOneOf(DetectionType.FullName, DetectionType.PossiblePersonalData);
        all.Where(d => d.Value == "Ad Soyad").Should().BeEmpty();
    }
    [Fact] public void Possible_RealFileRegression_NoFalsePositives()
    {
        var text = @"Ad Soyad: Ahmet Yılmaz
Telefon: 0555 123 45 67
Tesisat No: 12132133
E-posta: ahmet.yilmaz@example.invalid
Adres: Çankaya Mahallesi Atatürk Caddesi No: 12 Daire: 5
Doğum Tarihi: 14.03.1990
T.C. Kimlik No: 12345678901
Müşteri No: 847293

İşlem Bilgileri
Ankara Bölge Müdürlüğü
Müşteri Başvuru Formu
Dosya No: 2026/12345
Talep Evrakı";
        var all = _engine.Detect(Doc(text)).Value;
        // Definitive should be found
        all.Should().Contain(d => d.Type == DetectionType.Phone);
        all.Should().Contain(d => d.Type == DetectionType.TesisatNo);
        all.Should().Contain(d => d.Type == DetectionType.Email);
        all.Should().Contain(d => d.Type == DetectionType.Address);
        all.Should().Contain(d => d.Type == DetectionType.Date);
        all.Should().Contain(d => d.Type == DetectionType.TcKimlikNo);
        // False positives must NOT be Possible
        Possible(text).Where(d => d.Value == "Çankaya Mahallesi").Should().BeEmpty();
        Possible(text).Where(d => d.Value == "İşlem Bilgileri").Should().BeEmpty();
        Possible(text).Where(d => d.Value == "Doğum Tarihi").Should().BeEmpty();
        Possible(text).Where(d => d.Value == "Kimlik No").Should().BeEmpty();
        Possible(text).Where(d => d.Value == "Ankara Bölge Müdürlüğü").Should().BeEmpty();
        Possible(text).Where(d => d.Value == "Müşteri Başvuru Formu").Should().BeEmpty();
        // Ahmet Yılmaz should be FullName or Possible, not both
        var ahmet = all.Where(d => d.Value == "Ahmet Yılmaz").ToList();
        ahmet.Should().HaveCount(1);
    }
}
