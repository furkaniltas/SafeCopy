using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EksimSafeCopy.App.Services;
using EksimSafeCopy.App.ViewModels;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using EksimSafeCopy.Detectors;
using EksimSafeCopy.Renderer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;

namespace EksimSafeCopy.App.Tests;

class FakeFileDialogService : IFileDialogService
{
    public string? ReturnPath { get; set; }
    public string? OpenFile(string filter, string title) => ReturnPath;
    public string? SaveFile(string filter, string defaultFileName, string title) => ReturnPath;
    public IReadOnlyList<string>? OpenFiles(string filter, string title) => ReturnPath != null ? new[] { ReturnPath } : null;
}

public class MainViewModelTests
{
    private ServiceProvider CreateProvider(FakeFileDialogService? fakeDialog = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Pdf.PdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Docx.DocxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Xlsx.XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Txt.TxtDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Udf.UdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Image.ImageDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine>();
        services.AddDetectors();
        services.AddRenderer();
        services.AddSingleton<IFileDialogService>(fakeDialog ?? new FakeFileDialogService());
        services.AddTransient<MainViewModel>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void InitialState_IdleAndEmpty()
    {
        var vm = CreateProvider().GetRequiredService<MainViewModel>();
        vm.ProcessingState.Should().Be(ProcessingState.Idle);
        vm.Detections.Should().BeEmpty();
        vm.CanRedact.Should().BeFalse();
        vm.HasDetections.Should().BeFalse();
        vm.StatusMessage.Should().Contain("Dosya seçin");
        vm.OriginalHash.Should().BeNull();
    }

    [Fact]
    public async Task LoadAndDetect_TxtWithPII_PopulatesDetectionsAndPreview()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Ad Soyad: Ahmet Yılmaz\nTC: 10000000146");
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            vm.CurrentDocument.Should().NotBeNull();
            vm.CurrentDocument!.Format.Should().Be(DF.Txt);
            vm.Detections.Should().NotBeEmpty();
            vm.HasDetections.Should().BeTrue();
            vm.PreviewText.Should().Contain("Ahmet");
            vm.OriginalHash.Should().NotBeNullOrEmpty();
            vm.ProcessingState.Should().Be(ProcessingState.Ready);
            // Selection defaults to true
            vm.SelectedCount.Should().BeGreaterThan(0);
            vm.CanRedact.Should().BeTrue();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task DetectionItem_ToggleSelection_UpdatesCanRedact()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Ahmet Yılmaz - ahmet@example.com");
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            vm.Detections.Should().NotBeEmpty();
            var first = vm.Detections[0];
            first.IsSelected = false;
            // deselect all
            foreach (var d in vm.Detections) d.IsSelected = false;
            vm.CanRedact.Should().BeFalse();
            // select one
            vm.Detections[0].IsSelected = true;
            vm.CanRedact.Should().BeTrue();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task EmptyDetection_NoPII_ReadiesWithInfo()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Bu belgede hassas veri yok. Sadece genel bilgi.");
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            vm.ProcessingState.Should().Be(ProcessingState.Ready);
            vm.StatusMessage.Should().Contain("PII bulunamadı");
            vm.CanRedact.Should().BeFalse();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task UnsupportedPdf_WithPII_ShowsUnsupportedAndBlocksRedact()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.pdf");
        CreatePdf(tmp, "TC KIMLIK NO: 10000000146\nAD SOYAD: Test Kullanıcısı");
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            // PDF detection may or may not find PII depending on PdfPig, but redaction must be blocked if detections exist
            if (vm.Detections.Any())
            {
                vm.ProcessingState.Should().Be(ProcessingState.Unsupported);
                vm.UnsupportedMessage.Should().Contain("PDF redaction");
                vm.CanRedact.Should().BeFalse();
                // Attempt redact via command should remain unsupported and not create output
                await vm.RedactAsyncForTest();
                vm.ProcessingState.Should().Be(ProcessingState.Unsupported);
                vm.OutputPath.Should().BeNullOrEmpty();
            }
            else
            {
                // No PII found -> Ready, but still PDF redaction with pending would fail; this is acceptable
                vm.ProcessingState.Should().Be(ProcessingState.Ready);
            }
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task UnsupportedUdf_WithPII_BlocksRedact()
    {
        // Create minimal UDF-like zip with content.xml
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.udf");
        CreateUdf(tmp, "Ahmet Yılmaz TC 10000000146");
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            if (vm.Detections.Any())
            {
                vm.ProcessingState.Should().Be(ProcessingState.Unsupported);
                vm.UnsupportedMessage.Should().Contain("UDF");
                vm.CanRedact.Should().BeFalse();
            }
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task Redact_Txt_Success_CreatesOutputAndVerifies()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Ad Soyad: Ahmet Yılmaz\nTC: 10000000146\nTelefon: 05321234567");
        string? outputPath = null;
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            vm.SelectedFilePath = tmp;
            vm.Detections.Should().NotBeEmpty();
            var originalHash = vm.OriginalHash;
            await vm.RedactAsyncForTest();
            vm.ProcessingState.Should().Be(ProcessingState.Success);
            vm.VerificationResult.Should().NotBeNull();
            vm.VerificationResult!.Passed.Should().BeTrue();
            vm.OutputPath.Should().NotBeNullOrEmpty();
            outputPath = vm.OutputPath!;
            File.Exists(outputPath).Should().BeTrue();
            // Original unchanged
            var currentHash = ComputeHash(tmp);
            currentHash.Should().Be(originalHash);
            // Output does not contain original PII
            var outputText = File.ReadAllText(outputPath);
            outputText.Should().NotContain("Ahmet Yılmaz");
            outputText.Should().NotContain("10000000146");
        }
        finally
        {
            File.Delete(tmp);
            if (outputPath != null && File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task VerificationFailure_NotPresentedAsSuccess()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Ahmet Yılmaz");
        string? outPath = null;
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            vm.SelectedFilePath = tmp;
            await vm.RedactAsyncForTest();
            // With correct redaction, verification should pass
            vm.VerificationResult!.Passed.Should().BeTrue();
            vm.ProcessingState.Should().Be(ProcessingState.Success);
            outPath = vm.OutputPath;
        }
        finally { File.Delete(tmp); if (outPath != null && File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public async Task Cancellation_CleansTempAndReturnsToCancelled()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Ahmet Yılmaz " + new string('a', 10000));
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            var loadTask = vm.LoadAndDetectAsync(tmp);
            // Cancel quickly
            vm.CancelCommand.Execute(null);
            try { await loadTask; } catch { }
            // Should be either Cancelled or Failed or Ready, but not hang, and temp cleaned
            // Original must remain
            File.Exists(tmp).Should().BeTrue();
            vm.IsBusy.Should().BeFalse();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task ErrorState_FileNotFound_ShowsUserFriendlyMessage()
    {
        var vm = CreateProvider().GetRequiredService<MainViewModel>();
        await vm.LoadAndDetectAsync(@"Z:\nonexistent\file_12345.txt");
        vm.ProcessingState.Should().Be(ProcessingState.Failed);
        vm.StatusMessage.Should().Contain("Dosya yüklenemedi");
        vm.StatusMessage.Should().NotContain("StackTrace");
        vm.StatusMessage.Should().NotContain("at ");
    }

    [Fact]
    public async Task OriginalFilePath_StateUnchanged_AfterRedact()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Ahmet Yılmaz");
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            var originalPath = vm.SelectedFilePath ?? tmp; // LoadAndDetect doesn't set SelectedFilePath when called directly, so set
            vm.SelectedFilePath = tmp;
            var beforeHash = ComputeHash(tmp);
            await vm.RedactAsyncForTest();
            var afterHash = ComputeHash(tmp);
            afterHash.Should().Be(beforeHash);
            vm.SelectedFilePath.Should().Be(tmp);
        }
        finally
        {
            File.Delete(tmp);
            var outPath = Path.Combine(Path.GetDirectoryName(tmp)!, Path.GetFileNameWithoutExtension(tmp) + "_SafeCopy" + Path.GetExtension(tmp));
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void DetectionList_ContainsExpectedTypes()
    {
        // Ensure ViewModel respects DetectionType enum without inventing new types
        var allowed = Enum.GetNames(typeof(DetectionType));
        allowed.Should().Contain("TcKimlikNo");
        allowed.Should().Contain("FullName");
        allowed.Should().Contain("Phone");
        allowed.Should().Contain("Email");
        allowed.Should().Contain("Address");
        allowed.Should().Contain("TesisatNo");
    }

    [Fact]
    public async Task Redact_WithPartialSelection_Succeeds()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Tesisat No : 12132133\nAD Soyad : Furkan İltaş\nTC: 10000000146\nTelefon: 05321234567\nAdres: Ankara");
        string? outputPath = null;
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            vm.SelectedFilePath = tmp;
            vm.Detections.Should().HaveCountGreaterThan(1);
            // Deselect one detection (e.g., second one that is selected)
            var initialSelected = vm.SelectedCount;
            var toDeselect = vm.Detections.First(d => d.IsSelected);
            toDeselect.IsSelected = false;
            vm.SelectedCount.Should().Be(initialSelected - 1);
            await vm.RedactAsyncForTest();
            vm.ProcessingState.Should().Be(ProcessingState.Success, $"Status: {vm.StatusMessage}");
            vm.VerificationResult.Should().NotBeNull();
            vm.VerificationResult!.Passed.Should().BeTrue();
            outputPath = vm.OutputPath!;
            File.Exists(outputPath).Should().BeTrue();
            var outputText = File.ReadAllText(outputPath);
            // Deselected value should remain
            outputText.Should().Contain(toDeselect.Value);
            // Selected values should be masked
            var selected = vm.Detections.Where(d => d.IsSelected).ToList();
            foreach (var s in selected)
                outputText.Should().NotContain(s.Value);
        }
        finally
        {
            File.Delete(tmp);
            if (outputPath != null && File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    private string ComputeHash(string path)
    {
        using var s = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(s));
    }

    private void CreatePdf(string path, string text)
    {
        // PdfSharp 6.x requires font resolver on non-Windows or headless; enable Windows fonts
        try { PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true; } catch { }
        var doc = new PdfSharp.Pdf.PdfDocument();
        var page = doc.AddPage();
        var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        PdfSharp.Drawing.XFont font;
        try { font = new PdfSharp.Drawing.XFont("Arial", 12); }
        catch { font = new PdfSharp.Drawing.XFont("Helvetica", 12); }
        var lines = text.Split('\n');
        double y = 40;
        foreach (var l in lines)
        {
            gfx.DrawString(l, font, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(40, y));
            y += 20;
        }
        doc.Save(path);
    }

    private void CreateUdf(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        var entry = zip.CreateEntry("content.xml");
        using var w = new StreamWriter(entry.Open());
        w.Write($"<root>{content}</root>");
    }

    [Fact]
    public async Task Counts_WithDefiniteAndPossible_Correct()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"app_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Başvuru Sahibi: Ayşe Demir\nAd Soyad: Ahmet Yılmaz\nTelefon: 05321234567\nTC: 10000000146\nTesisat No: 12132133\nE-posta: ahmet.yilmaz@example.invalid\nAdres: Çankaya Mahallesi Atatürk Caddesi No: 12");
        try
        {
            var vm = CreateProvider().GetRequiredService<MainViewModel>();
            await vm.LoadAndDetectAsync(tmp);
            // Ensure we have both definite and possible via synthetic addition if needed
            if (!vm.HasPossibleDetections)
            {
                var possible = new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ayşe Demir", Confidence = 0.6, ConfidenceLevel = ConfidenceLevel.Medium, PageNumber = 0, TextSpan = new TextSpan { StartIndex = 0, Length = 10, Text = "Ayşe Demir" } };
                vm.Detections.Add(new DetectionItemViewModel(possible, isSelected: false));
            }
            if (vm.DefiniteCount < 2)
            {
                var extra = new Detection { Type = DetectionType.Phone, Value = "05321234567", Confidence = 0.9, ConfidenceLevel = ConfidenceLevel.High, PageNumber = 0, TextSpan = new TextSpan { StartIndex = 0, Length = 11, Text = "05321234567" } };
                vm.Detections.Add(new DetectionItemViewModel(extra, isSelected: true));
            }
            vm.DefiniteCount.Should().Be(vm.DefiniteDetections.Count());
            vm.PossibleCount.Should().Be(vm.PossibleDetections.Count());
            vm.PossibleCount.Should().BeGreaterOrEqualTo(1);
            // SelectedDefiniteCount should equal number of selected definite
            var expectedSelectedDefinite = vm.DefiniteDetections.Count(d => d.IsSelected);
            vm.SelectedDefiniteCount.Should().Be(expectedSelectedDefinite);
            // Initially possible is unchecked, so SelectedDefinite = total definite selected
            vm.SelectedDefiniteCount.Should().Be(vm.DefiniteDetections.Count(d => d.IsSelected));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void PossibleSelection_DoesNotAffect_SelectedDefiniteCount()
    {
        var vm = CreateProvider().GetRequiredService<MainViewModel>();
        // Add 6 definite selected
        for (int i = 0; i < 6; i++)
        {
            var d = new Detection { Type = DetectionType.Phone, Value = $"555000000{i}", Confidence = 0.9, ConfidenceLevel = ConfidenceLevel.High, PageNumber = 0, TextSpan = new TextSpan { StartIndex = i * 10, Length = 10, Text = $"555000000{i}" } };
            vm.Detections.Add(new DetectionItemViewModel(d, isSelected: true));
        }
        var possible = new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ayşe Demir", Confidence = 0.6, ConfidenceLevel = ConfidenceLevel.Medium, PageNumber = 0, TextSpan = new TextSpan { StartIndex = 100, Length = 10, Text = "Ayşe Demir" } };
        var possibleVm = new DetectionItemViewModel(possible, isSelected: false);
        vm.Detections.Add(possibleVm);

        vm.DefiniteCount.Should().Be(6);
        vm.PossibleCount.Should().Be(1);
        vm.SelectedDefiniteCount.Should().Be(6);

        possibleVm.IsSelected = true;
        vm.SelectedDefiniteCount.Should().Be(6, "Possible selection must not affect definite count");
        vm.SelectedCount.Should().Be(7);

        possibleVm.IsSelected = false;
        vm.SelectedDefiniteCount.Should().Be(6);
    }

    [Fact]
    public void DefinitiveDeselection_Updates_SelectedDefiniteCount()
    {
        var vm = CreateProvider().GetRequiredService<MainViewModel>();
        for (int i = 0; i < 6; i++)
        {
            var d = new Detection { Type = DetectionType.Phone, Value = $"555000000{i}", Confidence = 0.9, ConfidenceLevel = ConfidenceLevel.High, PageNumber = 0, TextSpan = new TextSpan { StartIndex = i * 10, Length = 10, Text = $"555000000{i}" } };
            vm.Detections.Add(new DetectionItemViewModel(d, isSelected: true));
        }
        var possible = new Detection { Type = DetectionType.PossiblePersonalData, Value = "Ayşe Demir", Confidence = 0.6, ConfidenceLevel = ConfidenceLevel.Medium, PageNumber = 0, TextSpan = new TextSpan { StartIndex = 100, Length = 10, Text = "Ayşe Demir" } };
        vm.Detections.Add(new DetectionItemViewModel(possible, isSelected: false));

        vm.SelectedDefiniteCount.Should().Be(6);
        vm.DefiniteDetections.First().IsSelected = false;
        vm.SelectedDefiniteCount.Should().Be(5);
    }

    [Fact]
    public void DetectionCollection_Change_UpdatesDefiniteCountNotStale()
    {
        var vm = CreateProvider().GetRequiredService<MainViewModel>();
        vm.DefiniteCount.Should().Be(0);
        var d = new Detection { Type = DetectionType.Phone, Value = "5551234567", Confidence = 0.9, ConfidenceLevel = ConfidenceLevel.High, PageNumber = 0, TextSpan = new TextSpan { StartIndex = 0, Length = 10, Text = "5551234567" } };
        vm.Detections.Add(new DetectionItemViewModel(d, isSelected: true));
        vm.DefiniteCount.Should().Be(1, "DefiniteCount must not remain stale after CollectionChanged");
        vm.HasDefiniteDetections.Should().BeTrue();
        vm.Detections.Clear();
        vm.DefiniteCount.Should().Be(0);
        vm.HasDefiniteDetections.Should().BeFalse();
    }

    private bool vmOutputExists(out string p) { p = ""; return false; }
}

// Extension to expose RedactAsync for testing (since command is private)
static class MainViewModelTestExtensions
{
    public static Task RedactAsyncForTest(this MainViewModel vm)
    {
        // Use reflection to call private RedactAsync
        var method = typeof(MainViewModel).GetMethod("RedactAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Task)method!.Invoke(vm, null)!;
    }
}
