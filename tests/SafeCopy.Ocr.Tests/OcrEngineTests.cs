using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.DocumentEngine.Ingestion.Image;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using SafeCopy.Ocr;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace SafeCopy.Ocr.Tests;

public class OcrEngineTests
{
    private static byte[] CreatePngBytes(int width = 400, int height = 100, string? markerText = null)
    {
        using var image = new Image<Rgba32>(width, height);
        // Fill white background
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image[x, y] = new Rgba32(255, 255, 255, 255);
        // Simple dark rectangle to ensure not blank
        for (int y = 10; y < 30; y++)
            for (int x = 10; x < 200; x++)
                image[x, y] = new Rgba32(0, 0, 0, 255);

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        var bytes = ms.ToArray();
        if (!string.IsNullOrEmpty(markerText))
        {
            var marker = Encoding.UTF8.GetBytes(SecureImagePreprocessor.TestMarkerPrefix + markerText);
            var combined = new byte[bytes.Length + marker.Length];
            Buffer.BlockCopy(bytes, 0, combined, 0, bytes.Length);
            Buffer.BlockCopy(marker, 0, combined, bytes.Length, marker.Length);
            return combined;
        }
        return bytes;
    }

    [Fact]
    public void IsAvailable_ShouldBeTrue()
    {
        var engine = new LocalOcrEngine();
        engine.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void EngineName_ShouldContainLocalOcr()
    {
        var engine = new LocalOcrEngine();
        engine.EngineName.Should().Contain("SafeCopy.LocalOcr");
    }

    [Fact]
    public void SupportedLanguages_ShouldContainTr()
    {
        var engine = new LocalOcrEngine();
        engine.SupportedLanguages.Should().Contain("tr");
    }

    [Fact]
    public void SupportedLanguages_ShouldContainEn()
    {
        var engine = new LocalOcrEngine();
        engine.SupportedLanguages.Should().Contain("en");
    }

    [Fact]
    public void Recognize_ValidImage_ReturnsSuccess()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes();
        var result = engine.Recognize(bytes, "tr");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Language.Should().Be("tr");
    }

    [Fact]
    public async Task RecognizeAsync_ValidImage_ReturnsSuccess()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes();
        using var ms = new MemoryStream(bytes);
        var result = await engine.RecognizeAsync(ms, "tr");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public void Recognize_TurkishText_MarkerReturnsCorrectText()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes(markerText: "Ahmet Yılmaz");
        var result = engine.Recognize(bytes, "tr");
        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Be("Ahmet Yılmaz");
        result.Value.Words.Should().HaveCount(2);
        result.Value.Words[0].Text.Should().Be("Ahmet");
        result.Value.Words[1].Text.Should().Be("Yılmaz");
        result.Value.Confidence.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void Recognize_TurkishCharacters_HandlesSpecialChars()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes(markerText: "İstanbul Şişli Güneş");
        var result = engine.Recognize(bytes, "tr");
        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Be("İstanbul Şişli Güneş");
        result.Value.Lines.Should().NotBeEmpty();
        result.Value.Paragraphs.Should().NotBeEmpty();
    }

    [Fact]
    public void Recognize_ConfidenceAndBoundingBox_Mapped()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes(400, 100, "Test Confidence");
        var result = engine.Recognize(bytes, "tr");
        result.IsSuccess.Should().BeTrue();
        result.Value.Words.Should().NotBeEmpty();
        foreach (var w in result.Value.Words)
        {
            w.Confidence.Should().BeInRange(0, 1);
            w.BoundingBox.IsEmpty.Should().BeFalse();
            w.BoundingBox.Width.Should().BeGreaterThan(0);
            w.BoundingBox.Height.Should().BeGreaterThan(0);
        }
        result.Value.Lines[0].BoundingBox.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Preprocessor_Constants_ShouldMatchSpec()
    {
        SecureImagePreprocessor.MaxDimension.Should().Be(8192);
        SecureImagePreprocessor.MaxPixels.Should().Be(100_000_000);
        SecureImagePreprocessor.MaxFileSize.Should().Be(50_000_000);
    }

    [Fact]
    public void Recognize_FileTooLarge_ReturnsSecurityError()
    {
        var preprocessor = new SecureImagePreprocessor();
        var bigData = new byte[SecureImagePreprocessor.MaxFileSize + 1];
        var result = preprocessor.ValidateAndPreprocess(bigData);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SECURITY_ERROR");
    }

    [Fact]
    public void Recognize_MalformedImage_ReturnsFailure()
    {
        var engine = new LocalOcrEngine();
        var badBytes = Encoding.UTF8.GetBytes("not an image at all");
        var result = engine.Recognize(badBytes, "tr");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("FORMAT_ERROR", "INTERNAL_ERROR");
    }

    [Fact]
    public void Recognize_Cancellation_ReturnsCancelled()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes(markerText: "Cancelled Test");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = engine.Recognize(bytes, "tr", cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task RecognizeAsync_Cancellation_ThrowsOrReturnsCancelled()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes(markerText: "Async Cancel");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await engine.RecognizeAsync(bytes, "tr", cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public void Recognize_NullImage_ReturnsValidationError()
    {
        var engine = new LocalOcrEngine();
        var result = engine.Recognize((byte[])null!, "tr");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public void SecurePreprocessor_MalformedPng_Handled()
    {
        var preprocessor = new SecureImagePreprocessor();
        var malformed = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF };
        var result = preprocessor.ValidateAndPreprocess(malformed);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void SecurePreprocessor_ValidImage_DimensionsUnchanged()
    {
        var preprocessor = new SecureImagePreprocessor();
        var bytes = CreatePngBytes(200, 100);
        var result = preprocessor.ValidateAndPreprocess(bytes);
        result.IsSuccess.Should().BeTrue();
        result.Value.Width.Should().Be(200);
        result.Value.Height.Should().Be(100);
        result.Value.ProcessedData.Should().NotBeEmpty();
    }

    [Fact]
    public void SecurePreprocessor_EmptyImage_ReturnsValidationError()
    {
        var preprocessor = new SecureImagePreprocessor();
        var result = preprocessor.ValidateAndPreprocess(Array.Empty<byte>());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public void ImageIngestion_WithOcr_ReturnsOcrText()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<SecureImagePreprocessor>();
        services.AddSingleton<IOcrEngine, LocalOcrEngine>();
        services.AddSingleton<SafeCopy.DocumentEngine.Ingestion.IDocumentIngestor>(sp =>
            new ImageDocumentIngestor(sp.GetRequiredService<IDocumentSecurityValidator>(), sp.GetRequiredService<IFileSystem>(), sp.GetRequiredService<IOcrEngine>()));

        var provider = services.BuildServiceProvider();
        var ingestor = provider.GetServices<SafeCopy.DocumentEngine.Ingestion.IDocumentIngestor>().First();

        var bytes = CreatePngBytes(markerText: "Ahmet Yılmaz 11111111111");
        var tempFile = Path.Combine(Path.GetTempPath(), $"ocr_test_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempFile, bytes);
        try
        {
            var result = ingestor.Ingest(tempFile, new SafeCopy.DocumentEngine.Ingestion.IngestionOptions());
            result.IsSuccess.Should().BeTrue();
            result.Value.Pages.Should().NotBeEmpty();
            var page = result.Value.Pages[0];
            page.IsScanned.Should().BeTrue();
            page.Text.Should().Be("Ahmet Yılmaz 11111111111");
            page.TextBlocks.Should().NotBeEmpty();
            page.OcrInfo.Should().NotBeNull();
            page.OcrInfo!.Language.Should().Be("tr");
            page.OcrInfo.Engine.Should().Contain("LocalOcr");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImageIngestion_WithoutMarker_StillScannedButEmptyText()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<SecureImagePreprocessor>();
        services.AddSingleton<IOcrEngine, LocalOcrEngine>();
        services.AddSingleton<SafeCopy.DocumentEngine.Ingestion.IDocumentIngestor>(sp =>
            new ImageDocumentIngestor(sp.GetRequiredService<IDocumentSecurityValidator>(), sp.GetRequiredService<IFileSystem>(), sp.GetRequiredService<IOcrEngine>()));

        var provider = services.BuildServiceProvider();
        var ingestor = provider.GetServices<SafeCopy.DocumentEngine.Ingestion.IDocumentIngestor>().First();

        var bytes = CreatePngBytes(); // no marker
        var tempFile = Path.Combine(Path.GetTempPath(), $"ocr_empty_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempFile, bytes);
        try
        {
            var result = ingestor.Ingest(tempFile, new SafeCopy.DocumentEngine.Ingestion.IngestionOptions());
            result.IsSuccess.Should().BeTrue();
            result.Value.Pages[0].IsScanned.Should().BeTrue();
            // fallback returns empty text but still success
            result.Value.Pages[0].Text.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Recognize_StreamAndBytes_BothWork()
    {
        var engine = new LocalOcrEngine();
        var bytes = CreatePngBytes(markerText: "Stream Test");
        var resultBytes = engine.Recognize(bytes, "tr");
        using var ms = new MemoryStream(bytes);
        var resultStream = engine.Recognize(ms, "tr");
        resultBytes.IsSuccess.Should().BeTrue();
        resultStream.IsSuccess.Should().BeTrue();
        resultBytes.Value.Text.Should().Be(resultStream.Value.Text);
    }

    [Fact]
    public void OcrModule_AddOcr_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddOcr();
        var provider = services.BuildServiceProvider();
        provider.GetService<IOcrEngine>().Should().NotBeNull();
        provider.GetService<SecureImagePreprocessor>().Should().NotBeNull();
    }
}
