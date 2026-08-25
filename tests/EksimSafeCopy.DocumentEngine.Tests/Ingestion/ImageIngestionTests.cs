using System.IO;
using System.Text;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Image;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DocEngine = global::EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DocModel = global::EksimSafeCopy.Core.Models.Document;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.DocumentEngine.Tests.Ingestion;

public class ImageIngestionTests
{
    private readonly IDocumentEngine _documentEngine;

public ImageIngestionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IDocumentIngestor, ImageDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocEngine>();
        
        var provider = services.BuildServiceProvider();
        _documentEngine = provider.GetRequiredService<IDocumentEngine>();
    }

    [Fact]
    public void DetectFormat_PngFile_ReturnsPng()
    {
        var tempFile = CreateTempPng();

        try
        {
            var result = _documentEngine.DetectFormat(tempFile);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DF.Png);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFormat_JpegFile_ReturnsJpeg()
    {
        var tempFile = CreateTempJpeg();

        try
        {
            var result = _documentEngine.DetectFormat(tempFile);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DF.Jpeg);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ValidPng_ReturnsDocument()
    {
        var tempFile = CreateTempPng();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Format.Should().Be(DF.Png);
            result.Value.Pages.Should().NotBeEmpty();
            result.Value.Pages[0].IsScanned.Should().BeTrue();
            result.Value.Pages[0].Images.Should().NotBeEmpty();
            result.Value.Pages[0].Images[0].Width.Should().BeGreaterThan(0);
            result.Value.Pages[0].Images[0].Height.Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_Jpeg_ReturnsDocument()
    {
        var tempFile = CreateTempJpeg();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Format.Should().Be(DF.Jpeg);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsFailure()
    {
        var result = _documentEngine.Load("nonexistent.png");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    private string CreateTempPng()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.png");

        using (var image = new Image<Rgba32>(100, 100))
        {
            // Create a simple test image
            for (int y = 0; y < 100; y++)
            {
                for (int x = 0; x < 100; x++)
                {
                    image[x, y] = new Rgba32((byte)(x * 2), (byte)(y * 2), 128, 255);
                }
            }
            image.SaveAsPng(tempFile);
        }

        return tempFile;
    }

    private string CreateTempJpeg()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.jpg");

        using (var image = new Image<Rgba32>(100, 100))
        {
            for (int y = 0; y < 100; y++)
            {
                for (int x = 0; x < 100; x++)
                {
                    image[x, y] = new Rgba32(255, (byte)x, (byte)y, 255);
                }
            }
            image.SaveAsJpeg(tempFile);
        }

        return tempFile;
    }
}






