using System.IO;
using System.Text;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.DocumentEngine.Ingestion;
using SafeCopy.DocumentEngine.Ingestion.Txt;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using DocEngine = global::SafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DocModel = global::SafeCopy.Core.Models.Document;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SafeCopy.DocumentEngine.Tests.Ingestion;

public class TxtIngestionTests
{
    private readonly IDocumentEngine _documentEngine;

public TxtIngestionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IDocumentIngestor, TxtDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocEngine>();
        
        var provider = services.BuildServiceProvider();
        _documentEngine = provider.GetRequiredService<IDocumentEngine>();
    }

    [Fact]
    public void DetectFormat_TxtFile_ReturnsTxt()
    {
        var tempFile = CreateTempTxt();

        try
        {
            var result = _documentEngine.DetectFormat(tempFile);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DF.Txt);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ValidTxt_ReturnsDocument()
    {
        var tempFile = CreateTempTxt();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Format.Should().Be(DF.Txt);
            result.Value.Pages.Should().NotBeEmpty();
            result.Value.Source.FilePath.Should().Be(tempFile);
            result.Value.Source.FileHash.Should().NotBeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

[Fact]
    public void Load_TurkishEncoding_ReturnsCorrectText()
    {
        var tempFile = CreateTurkishTxt();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            var allText = string.Join("\n", result.Value!.Pages.Select(p => p.Text));
            allText.Should().Contain("Ahmet Yılmaz");
            allText.Should().Contain("İstanbul");
            allText.Should().Contain("ĞüşİÖÇ");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

[Fact]
    public void Load_Utf8WithBom_ReturnsCorrectText()
    {
        var tempFile = CreateUtf8WithBom();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            var allText = string.Join("\n", result.Value!.Pages.Select(p => p.Text));
            allText.Should().Contain("Test");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsFailure()
    {
        var result = _documentEngine.Load("nonexistent.txt");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    private string CreateTempTxt()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Test TXT Content\nLine 2\nLine 3 with Ahmet Yılmaz");
        return tempFile;
    }

    private string CreateTurkishTxt()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"turkish_{Guid.NewGuid():N}.txt");
        var content = "Ahmet Yılmaz\nİstanbul'da yaşıyor\nTC Kimlik: 11111111111\nÖzel karakterler: ĞüşİÖÇğüşıöç";
        File.WriteAllText(tempFile, content, Encoding.UTF8);
        return tempFile;
    }

    private string CreateUtf8WithBom()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"utf8bom_{Guid.NewGuid():N}.txt");
        var content = "Test UTF-8 BOM content";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF };
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var allBytes = bytes.Concat(contentBytes).ToArray();
        File.WriteAllBytes(tempFile, allBytes);
        return tempFile;
    }
}






