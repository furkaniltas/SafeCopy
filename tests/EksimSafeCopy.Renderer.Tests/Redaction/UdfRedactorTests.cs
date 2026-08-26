using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using EksimSafeCopy.Renderer.Redaction;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.Renderer.Tests.Redaction;

public class UdfRedactorTests
{
    private readonly IRedactor _redactor;

    public UdfRedactorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IRedactor, UdfRedactor>();
        var provider = services.BuildServiceProvider();
        _redactor = provider.GetRequiredService<IRedactor>();
    }

    [Fact]
    public void Redact_WithPendingOperations_ReturnsSecurityError()
    {
        var udfBytes = CreateMinimalUdf("Ahmet Yılmaz TC 11111111111");
        var plan = CreatePlanWithPending("Ahmet Yılmaz");
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SECURITY_ERROR");
        result.Error.Message.Should().Contain("UDF redaction desteklenmiyor");
    }

    [Fact]
    public void Redact_WithEmptyOperations_ReturnsSuccess()
    {
        var udfBytes = CreateMinimalUdf("Clean content without PII");
        var plan = CreateEmptyPlan();
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(udfBytes);
    }

    [Fact]
    public void Redact_RedactToFile_WithPending_FailsAndDoesNotCreateOutput()
    {
        var udfBytes = CreateMinimalUdf("Secret 11111111111");
        var tempInput = Path.Combine(Path.GetTempPath(), $"udf_in_{Guid.NewGuid():N}.udf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"udf_out_{Guid.NewGuid():N}.udf");
        File.WriteAllBytes(tempInput, udfBytes);
        var hashBefore = ComputeHash(tempInput);
        try
        {
            var plan = CreatePlanWithPending("11111111111");
            var result = _redactor.RedactToFile(tempInput, tempOutput, plan, new RenderOptions());
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("SECURITY_ERROR");
            File.Exists(tempOutput).Should().BeFalse();
            ComputeHash(tempInput).Should().Be(hashBefore);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
            if (File.Exists(tempOutput + ".tmp")) File.Delete(tempOutput + ".tmp");
        }
    }

    [Fact]
    public void Redact_RedactToFile_WithEmpty_SucceedsAndHashUnchanged()
    {
        var udfBytes = CreateMinimalUdf("Clean");
        var tempInput = Path.Combine(Path.GetTempPath(), $"udf_in_{Guid.NewGuid():N}.udf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"udf_out_{Guid.NewGuid():N}.udf");
        File.WriteAllBytes(tempInput, udfBytes);
        var hashBefore = ComputeHash(tempInput);
        try
        {
            var plan = CreateEmptyPlan();
            var result = _redactor.RedactToFile(tempInput, tempOutput, plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            File.Exists(tempOutput).Should().BeTrue();
            ComputeHash(tempInput).Should().Be(hashBefore);
            File.ReadAllBytes(tempOutput).Should().Equal(udfBytes);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public void Redact_MultiplePendingOps_ReturnsSecurityError()
    {
        var udfBytes = CreateMinimalUdf("Ahmet Yılmaz 11111111111");
        var plan = new RedactionPlan
        {
            DocumentId = "udf-multi",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" },
                new RedactionOperation { DetectionId = "2", DetectionType = DetectionType.TcKimlikNo, TextSpan = new TextSpan { StartIndex = 13, Length = 11, Text = "11111111111" }, State = RedactionOperationState.Pending, ReplacementText = "[TC_KIMLIK_NO]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SECURITY_ERROR");
    }

    [Fact]
    public void Redact_SkippedState_NotPending_ReturnsSuccess()
    {
        var udfBytes = CreateMinimalUdf("Ahmet Yılmaz");
        var plan = new RedactionPlan
        {
            DocumentId = "udf-skipped",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Skipped, ReplacementText = "[AD SOYAD]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        // No pending ops, should succeed
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Redact_UdfTargetFormat_IsUdf()
    {
        _redactor.TargetFormat.Should().Be(DF.Udf);
    }

    private byte[] CreateMinimalUdf(string content)
    {
        // Minimal UDF zip with content.xml - mimics real UDF structure
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("content.xml");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write($"<content><body>{System.Security.SecurityElement.Escape(content)}</body></content>");
        }
        return ms.ToArray();
    }

    private RedactionPlan CreatePlanWithPending(string text)
    {
        var detection = new Detection { Type = DetectionType.FullName, Value = text, TextSpan = new TextSpan { StartIndex = 0, Length = text.Length, Text = text } };
        var op = new RedactionOperation
        {
            DetectionId = detection.Id,
            DetectionType = detection.Type,
            TextSpan = detection.TextSpan,
            PageNumber = 1,
            Strategy = RedactionStrategy.TypeLabel,
            ReplacementText = "[AD SOYAD]",
            State = RedactionOperationState.Pending
        };
        return new RedactionPlan { DocumentId = "udf-test", Operations = new[] { op }, Format = DF.Udf };
    }

    private RedactionPlan CreateEmptyPlan()
    {
        return new RedactionPlan { DocumentId = "udf-empty", Operations = Array.Empty<RedactionOperation>(), Format = DF.Udf };
    }

    private string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }
}
