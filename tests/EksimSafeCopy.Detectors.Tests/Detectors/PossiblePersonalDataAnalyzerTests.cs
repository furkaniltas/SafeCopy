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

    [Fact] public void Possible_BasvuruSahibi_AyseDemir() => Possible("Basvuru Sahibi: Ayse Demir").Should().ContainSingle(d => d.Value == "Ayse Demir");
    [Fact] public void Possible_MusteriAdi_MehmetKaya() => Possible("Musteri Adi: Mehmet Kaya").Should().ContainSingle(d => d.Value == "Mehmet Kaya");
    [Fact] public void Possible_IlgiliKisi_AhmetYilmaz() => Possible("Ilgili Kisi: Ahmet Yilmaz").Should().ContainSingle(d => d.Value == "Ahmet Yilmaz");
    [Fact] public void Possible_Diyarbakir_NotDetected() => Possible("DIYARBAKIR ICRA DAIRESI").Should().BeEmpty();
    [Fact] public void Possible_NeEsas_NotDetected() => Possible("NE ESAS TALEP EVRAKI").Should().BeEmpty();
    [Fact] public void Possible_Takibin_NotDetected() => Possible("Takibin Kesinlestirilmesini").Should().BeEmpty();
    [Fact] public void Possible_AnkaraBolge_NotDetected() => Possible("Ankara Bolge Mudurlugu").Should().BeEmpty();
    [Fact] public void Possible_MusteriBasvuruFormu_NotDetected() => Possible("Musteri Basvuru Formu").Should().BeEmpty();
    [Fact] public void Possible_DogumTarihiLabel_NotDetected() => Possible("Dogum Tarihi").Should().BeEmpty();
    [Fact] public void Possible_KimlikNoLabel_NotDetected() => Possible("Kimlik No").Should().BeEmpty();
    [Fact] public void Possible_AdSoyadLabel_NotDetected() => Possible("Ad Soyad").Should().BeEmpty();
    [Fact] public void Possible_SabriGoclu_FullNameNotPossible() {
        var all = _engine.Detect(Doc("SABRI GOCLU")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "SABRI GOCLU");
        all.Should().NotContain(d => d.Type == DetectionType.PossiblePersonalData);
    }
    [Fact] public void Possible_FurkanIltas_FullNameNotPossible() {
        var all = _engine.Detect(Doc("Furkan Iltas")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.FullName && d.Value == "Furkan Iltas");
        all.Should().NotContain(d => d.Type == DetectionType.PossiblePersonalData);
    }
    [Fact] public void Possible_TcProximity_AyseDemir() {
        var all = _engine.Detect(Doc("TC Kimlik No: 12345678901\nBasvuru Sahibi: Ayse Demir")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.TcKimlikNo);
        all.Where(d => d.Type == DetectionType.PossiblePersonalData).Should().ContainSingle(d => d.Value == "Ayse Demir");
    }
    [Fact] public void Possible_DateLabel_NotPossibleButDateIsDefinite() {
        var all = _engine.Detect(Doc("Dogum Tarihi: 12.04.1988")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "12.04.1988");
        all.Where(d => d.Type == DetectionType.PossiblePersonalData).Should().BeEmpty();
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
        Possible("Ayse Demir").Should().BeEmpty();
    }
    [Fact] public void Possible_FarProximity_NotDetected() {
        var filler = new string('x', 300);
        Possible($"TC Kimlik No: 12345678901\n{filler}\nAhmet Yilmaz").Where(d => d.Value == "Ahmet Yilmaz").Should().BeEmpty();
    }
}
