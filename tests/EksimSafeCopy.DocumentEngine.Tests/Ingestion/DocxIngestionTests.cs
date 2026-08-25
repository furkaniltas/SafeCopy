using System.Text;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Docx;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DocEngine = global::EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DocModel = global::EksimSafeCopy.Core.Models.Document;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Xunit;

namespace EksimSafeCopy.DocumentEngine.Tests.Ingestion;

public class DocxIngestionTests
{
    private readonly IDocumentEngine _documentEngine;

    public DocxIngestionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, global::EksimSafeCopy.Infrastructure.FileSystem>();
        services.AddSingleton<IDocumentIngestor, DocxDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocEngine>();

        var provider = services.BuildServiceProvider();
        _documentEngine = provider.GetRequiredService<IDocumentEngine>();
    }

    [Fact]
    public void DetectFormat_DocxFile_ReturnsDocx()
    {
        var tempFile = CreateTempDocx();

        try
        {
            var result = _documentEngine.DetectFormat(tempFile);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DocumentFormat.Docx);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ValidDocx_ReturnsDocument()
    {
        var tempFile = CreateTempDocx();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Format.Should().Be(DocumentFormat.Docx);
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
    public void Load_NonExistentFile_ReturnsFailure()
    {
        var result = _documentEngine.Load("nonexistent.docx");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    private string CreateTempDocx()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");

        using (var document = WordprocessingDocument.Create(tempFile, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(
                    new Paragraph(
                        new Run(new Text("Test DOCX Content"))
                    ),
                    new Paragraph(
                        new Run(new Text("Second paragraph with Turkish: Ahmet Yılmaz"))
                    )
                )
            );
        }

        return tempFile;
    }
}