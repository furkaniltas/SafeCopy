using System.IO;
using System.Text;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Docx;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DocEngine = global::EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DocModel = global::EksimSafeCopy.Core.Models.Document;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using Ox = DocumentFormat.OpenXml;
using OxPackaging = DocumentFormat.OpenXml.Packaging;
using OxWord = DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.DocumentEngine.Tests.Ingestion;

public class DocxIngestionTests
{
    private readonly IDocumentEngine _documentEngine;

    public DocxIngestionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
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
            result.Value.Should().Be(DF.Docx);
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
            result.Value!.Format.Should().Be(DF.Docx);
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

        using (var wordDoc = OxPackaging.WordprocessingDocument.Create(tempFile, Ox.WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new OxWord.Document(
                new OxWord.Body(
                    new OxWord.Paragraph(
                        new OxWord.Run(new OxWord.Text("Test DOCX Content"))
                    ),
                    new OxWord.Paragraph(
                        new OxWord.Run(new OxWord.Text("Second paragraph with Turkish: Ahmet Yılmaz"))
                    )
                )
            );
        }

        return tempFile;
    }
}