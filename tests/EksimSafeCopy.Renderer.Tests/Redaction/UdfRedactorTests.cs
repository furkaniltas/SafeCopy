using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors;
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

    // Legacy tests adapted to real redaction (FAZ 8.1)
    [Fact]
    public void Redact_WithPendingOperations_ReturnsSuccessAndMasks()
    {
        var udfBytes = CreateMinimalUdf("Ahmet Yılmaz TC 11111111111");
        var plan = CreatePlanWithPending("Ahmet Yılmaz");
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        // Content should be masked, not equal
        result.Value.Should().NotEqual(udfBytes);
        var outText = ExtractContentText(result.Value);
        outText.Should().Contain("[AD SOYAD]");
        outText.Should().NotContain("Ahmet Yılmaz");
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
    public void Redact_RedactToFile_WithPending_SucceedsAndHashUnchanged()
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
            result.IsSuccess.Should().BeTrue();
            File.Exists(tempOutput).Should().BeTrue();
            ComputeHash(tempInput).Should().Be(hashBefore);
            var outText = ExtractContentText(File.ReadAllBytes(tempOutput));
            outText.Should().Contain("[AD SOYAD]");
            File.Exists(tempOutput + ".tmp").Should().BeFalse();
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
    public void Redact_MultiplePendingOps_SucceedsWithAllMasked()
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
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().Contain("[AD SOYAD]");
        outText.Should().Contain("[TC_KIMLIK_NO]");
        outText.Should().NotContain("Ahmet Yılmaz");
        outText.Should().NotContain("11111111111");
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
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Redact_UdfTargetFormat_IsUdf()
    {
        _redactor.TargetFormat.Should().Be(DF.Udf);
    }

    // A. Anonymized realistic fixture (template/content/elements)
    [Fact]
    public void Redact_RealisticAnonymizedFixture_FullMask()
    {
        var content = "T.C.\nDİYARBAKIR İCRA DAİRESİ'NE\nTALEP EVRAKI\nİşlem Yapılacak Taraf Adı: Ahmet Yılmaz, HATİP oğlu, 18/08/1969 doğum tarihli;";
        var udfBytes = CreateRealisticUdf(content);
        var plan = new RedactionPlan
        {
            DocumentId = "realistic",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = "Ahmet Yılmaz".Length, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" },
                new RedactionOperation { DetectionId = "2", DetectionType = DetectionType.Date, TextSpan = new TextSpan { StartIndex = content.IndexOf("18/08/1969"), Length = "18/08/1969".Length, Text = "18/08/1969" }, State = RedactionOperationState.Pending, ReplacementText = "[TARIH]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().NotContain("Ahmet Yılmaz");
        outText.Should().NotContain("18/08/1969");
        outText.Should().Contain("[AD SOYAD]");
        outText.Should().Contain("[TARIH]");
        outText.Should().Contain("DİYARBAKIR");
    }

    // B. content.xml parse preserved
    [Fact]
    public void Redact_ContentXmlParse_PropertiesStylesPreserved()
    {
        var content = "Ahmet Yılmaz 11111111111";
        var udfBytes = CreateRealisticUdf(content);
        var plan = CreatePlanWithPending("Ahmet Yılmaz");
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var xml = ExtractContentXml(result.Value);
        var xdoc = XDocument.Parse(xml);
        xdoc.Root!.Element("properties").Should().NotBeNull();
        xdoc.Root!.Element("styles").Should().NotBeNull();
        xdoc.Root!.Element("elements").Should().NotBeNull();
    }

    // C. FullMask
    [Fact]
    public void Redact_FullMask_UsesReplacementText()
    {
        var content = "SABRİ GÖÇLÜ TC 11111111111";
        var udfBytes = CreateRealisticUdf(content);
        var plan = new RedactionPlan
        {
            DocumentId = "fullmask",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 10, Text = "SABRİ GÖÇLÜ" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().Be("[AD SOYAD] TC 11111111111");
    }

    // D. PartialMask
    [Fact]
    public void Redact_PartialMask_UsesPartialPolicy()
    {
        var content = "Ahmet Yılmaz 11111111111";
        var udfBytes = CreateRealisticUdf(content);
        // Simulate PartialMask replacement as planner would (*******1111 style)
        var partial = "*******1111";
        var plan = new RedactionPlan
        {
            DocumentId = "partial",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.TcKimlikNo, TextSpan = new TextSpan { StartIndex = 13, Length = 11, Text = "11111111111" }, State = RedactionOperationState.Pending, ReplacementText = partial }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().Contain(partial);
        outText.Should().NotContain("11111111111");
    }

    // E. Turkish roundtrip
    [Fact]
    public void Redact_TurkishCharacters_RoundtripPreserved()
    {
        var content = "İşlem Yapılacak Taraf Adı: Şehmuz ÖÇALAN, Diyarbakır";
        var udfBytes = CreateRealisticUdf(content);
        var plan = CreatePlanWithPending("Şehmuz ÖÇALAN");
        // Adjust span to actual index
        plan = new RedactionPlan
        {
            DocumentId = "turkish",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = content.IndexOf("Şehmuz ÖÇALAN"), Length = "Şehmuz ÖÇALAN".Length, Text = "Şehmuz ÖÇALAN" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().Contain("Diyarbakır");
        outText.Should().Contain("[AD SOYAD]");
        outText.Should().NotContain("Şehmuz ÖÇALAN");
        // Ensure UTF8 not corrupted
        outText.Should().Contain("İşlem");
    }

    // F. çoklu PII
    [Fact]
    public void Redact_MultiplePII_AllMasked()
    {
        var content = "Ahmet Yılmaz 11111111111 18/08/1969 ahmet@example.com";
        var udfBytes = CreateRealisticUdf(content);
        var ops = new[]
        {
            new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" },
            new RedactionOperation { DetectionId = "2", DetectionType = DetectionType.TcKimlikNo, TextSpan = new TextSpan { StartIndex = content.IndexOf("11111111111"), Length = 11, Text = "11111111111" }, State = RedactionOperationState.Pending, ReplacementText = "[TC]" },
            new RedactionOperation { DetectionId = "3", DetectionType = DetectionType.Date, TextSpan = new TextSpan { StartIndex = content.IndexOf("18/08/1969"), Length = 10, Text = "18/08/1969" }, State = RedactionOperationState.Pending, ReplacementText = "[TARIH]" },
            new RedactionOperation { DetectionId = "4", DetectionType = DetectionType.Email, TextSpan = new TextSpan { StartIndex = content.IndexOf("ahmet@example.com"), Length = 17, Text = "ahmet@example.com" }, State = RedactionOperationState.Pending, ReplacementText = "[EMAIL]" }
        };
        var plan = new RedactionPlan { DocumentId = "multi", Format = DF.Udf, Operations = ops };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().NotContain("Ahmet Yılmaz");
        outText.Should().NotContain("11111111111");
        outText.Should().NotContain("18/08/1969");
        outText.Should().NotContain("ahmet@example.com");
    }

    // G. aynı paragraph içinde birden fazla PII
    [Fact]
    public void Redact_SameParagraph_MultiplePII()
    {
        var content = "Ad: Ahmet Yılmaz TCKN: 11111111111";
        var udfBytes = CreateRealisticUdf(content);
        var plan = new RedactionPlan
        {
            DocumentId = "samepara",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "[AD]" },
                new RedactionOperation { DetectionId = "2", DetectionType = DetectionType.TcKimlikNo, TextSpan = new TextSpan { StartIndex = content.IndexOf("11111111111"), Length = 11, Text = "11111111111" }, State = RedactionOperationState.Pending, ReplacementText = "[TC]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().Be("Ad: [AD] TCKN: [TC]");
    }

    // H. farklı uzunlukta replacement
    [Fact]
    public void Redact_DifferentLengthReplacement_OffsetsUpdated()
    {
        var content = "SABRİ GÖÇLÜ HATİP 18/08/1969";
        var udfBytes = CreateUdfWithMultiElement(content);
        var plan = new RedactionPlan
        {
            DocumentId = "difflen",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 10, Text = "SABRİ GÖÇLÜ" }, State = RedactionOperationState.Pending, ReplacementText = "[NAME]" } // 6 vs 10 delta -4
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().Be("[NAME] HATİP 18/08/1969");
        // Validate offsets
        var xml = ExtractContentXml(result.Value);
        ValidateOffsets(xml, outText);
    }

    // I. element offset consistency
    [Fact]
    public void Redact_ElementOffsetConsistency_AfterRedaction()
    {
        var content = "T.C. DİYARBAKIR İCRA DAİRESİ'NE TALEP EVRAKI Ahmet Yılmaz";
        var udfBytes = CreateUdfWithMultiElement(content);
        var plan = new RedactionPlan
        {
            DocumentId = "offset",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        var xml = ExtractContentXml(result.Value);
        ValidateOffsets(xml, outText);
        // Also check no negative offsets
        var xdoc = XDocument.Parse(xml);
        var els = xdoc.Root!.Element("elements")!.Descendants().Where(e => e.Attribute("startOffset") != null);
        foreach (var e in els)
        {
            int so = int.Parse(e.Attribute("startOffset")!.Value);
            int len = int.Parse((e.Attribute("length") ?? e.Attribute("len"))!.Value);
            so.Should().BeGreaterThanOrEqualTo(0);
            len.Should().BeGreaterThanOrEqualTo(0);
            (so + len).Should().BeLessThanOrEqualTo(outText.Length + 2); // allow trailing gap 1-2
        }
    }

    // J. malformed XML
    [Fact]
    public void Redact_MalformedXml_ReturnsFormatError()
    {
        var udfBytes = CreateMalformedUdf();
        var plan = CreatePlanWithPending("test");
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FORMAT_ERROR");
    }

    // K. missing content.xml
    [Fact]
    public void Redact_MissingContentXml_ReturnsFormatError()
    {
        var udfBytes = CreateUdfMissingContentXml();
        var plan = CreatePlanWithPending("test");
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FORMAT_ERROR");
    }

    // L. corrupt ZIP
    [Fact]
    public void Redact_CorruptZip_ReturnsFormatError()
    {
        var udfBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0xFF, 0xFF };
        var plan = CreatePlanWithPending("test");
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
    }

    // M. original hash unchanged (via RedactToFile)
    [Fact]
    public void Redact_OriginalHashUnchanged_AfterRedactToFile()
    {
        var content = "Ahmet Yılmaz 11111111111";
        var udfBytes = CreateRealisticUdf(content);
        var tempIn = Path.Combine(Path.GetTempPath(), $"udf_m_{Guid.NewGuid():N}.udf");
        var tempOut = Path.Combine(Path.GetTempPath(), $"udf_mo_{Guid.NewGuid():N}.udf");
        File.WriteAllBytes(tempIn, udfBytes);
        var before = ComputeHash(tempIn);
        try
        {
            var plan = new RedactionPlan
            {
                DocumentId = "hash",
                Format = DF.Udf,
                Operations = new[]
                {
                    new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "[AD]" }
                }
            };
            var result = _redactor.RedactToFile(tempIn, tempOut, plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            ComputeHash(tempIn).Should().Be(before);
            File.Exists(tempOut).Should().BeTrue();
            File.Exists(tempOut + ".tmp").Should().BeFalse();
        }
        finally { if (File.Exists(tempIn)) File.Delete(tempIn); if (File.Exists(tempOut)) File.Delete(tempOut); if (File.Exists(tempOut + ".tmp")) File.Delete(tempOut + ".tmp"); }
    }

    // N. output tekrar ingest
    [Fact]
    public void Redact_OutputCanBeReIngested()
    {
        var content = "SABRİ GÖÇLÜ 18/08/1969";
        var udfBytes = CreateRealisticUdf(content);
        var plan = new RedactionPlan
        {
            DocumentId = "reingest",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 10, Text = "SABRİ GÖÇLÜ" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        // Re-ingest via UdfDocumentIngestor
        var tempOut = Path.Combine(Path.GetTempPath(), $"udf_re_{Guid.NewGuid():N}.udf.zip");
        File.WriteAllBytes(tempOut, result.Value);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new DocumentSecurityOptions());
            services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<EksimSafeCopy.DocumentEngine.Ingestion.IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Udf.UdfDocumentIngestor>();
            services.AddSingleton<EksimSafeCopy.Core.Abstractions.IDocumentEngine, EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine>();
            var sp = services.BuildServiceProvider();
            var engine = sp.GetRequiredService<EksimSafeCopy.Core.Abstractions.IDocumentEngine>();
            var load = engine.Load(tempOut);
            load.IsSuccess.Should().BeTrue();
            load.Value.Pages[0].Text.Should().Contain("[AD SOYAD]");
            load.Value.Pages[0].Text.Should().NotContain("SABRİ GÖÇLÜ");
        }
        finally { if (File.Exists(tempOut)) File.Delete(tempOut); }
    }

    // O. verification residual
    [Fact]
    public void Redact_VerificationResidual_ShouldBeZero()
    {
        var content = "Ahmet Yılmaz 11111111111 ahmet@example.com";
        var udfBytes = CreateRealisticUdf(content);
        var plan = new RedactionPlan
        {
            DocumentId = "verify",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "[AD SOYAD]" },
                new RedactionOperation { DetectionId = "2", DetectionType = DetectionType.TcKimlikNo, TextSpan = new TextSpan { StartIndex = content.IndexOf("11111111111"), Length = 11, Text = "11111111111" }, State = RedactionOperationState.Pending, ReplacementText = "[TC]" },
                new RedactionOperation { DetectionId = "3", DetectionType = DetectionType.Email, TextSpan = new TextSpan { StartIndex = content.IndexOf("ahmet@example.com"), Length = 17, Text = "ahmet@example.com" }, State = RedactionOperationState.Pending, ReplacementText = "[EMAIL]" }
            }
        };
        var redact = _redactor.Redact(udfBytes, plan, new RenderOptions());
        redact.IsSuccess.Should().BeTrue();
        var tempOut = Path.Combine(Path.GetTempPath(), $"udf_v_{Guid.NewGuid():N}.udf.zip");
        File.WriteAllBytes(tempOut, redact.Value);
        try
        {
            // Verify via re-ingest and detection: original values should not be found
            var services = new ServiceCollection();
            services.AddSingleton(new DocumentSecurityOptions());
            services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<EksimSafeCopy.DocumentEngine.Ingestion.IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Udf.UdfDocumentIngestor>();
            services.AddSingleton<EksimSafeCopy.Core.Abstractions.IDocumentEngine, EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine>();
            services.AddDetectors();
            var sp = services.BuildServiceProvider();
            var engine = sp.GetRequiredService<EksimSafeCopy.Core.Abstractions.IDocumentEngine>();
            var detEngine = sp.GetRequiredService<EksimSafeCopy.Core.Abstractions.IDetectionEngine>();
            var doc = engine.Load(tempOut).Value;
            var dets = detEngine.Detect(doc).Value;
            dets.Any(d => d.Value == "Ahmet Yılmaz").Should().BeFalse();
            dets.Any(d => d.Value == "11111111111").Should().BeFalse();
            dets.Any(d => d.Value == "ahmet@example.com").Should().BeFalse();
        }
        finally { if (File.Exists(tempOut)) File.Delete(tempOut); }
    }

    // 12. Cross-element safety
    [Fact]
    public void Redact_CrossParagraph_Span_ShouldFailSecure()
    {
        // Two paragraphs, span crosses paragraph boundary
        var udfBytes = CreateCrossParagraphUdf();
        var plan = new RedactionPlan
        {
            DocumentId = "crosspara",
            Format = DF.Udf,
            Operations = new[]
            {
                // Span includes newline across paragraphs: "LINE1\nPARA2"
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 6, Length = 11, Text = "LINE1\nPARA2" }, State = RedactionOperationState.Pending, ReplacementText = "[X]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SECURITY_ERROR");
    }

    [Fact]
    public void Redact_CrossElementWithinSameParagraph_ShouldSucceed()
    {
        var content = "SABRİ GÖÇLÜ HATİP";
        var udfBytes = CreateUdfWithMultiElement(content); // splits into multiple elements within single para
        var plan = new RedactionPlan
        {
            DocumentId = "crossWithin",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 10, Text = "SABRİ GÖÇLÜ" }, State = RedactionOperationState.Pending, ReplacementText = "[AD]" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsSuccess.Should().BeTrue();
        var outText = ExtractContentText(result.Value);
        outText.Should().Contain("[AD]");
    }

    // Security: ZIP Slip
    [Fact]
    public void Redact_ZipSlip_ShouldFail()
    {
        var udfBytes = CreateUdfWithUnsafeEntry("../evil.txt");
        var plan = CreatePlanWithPending("test");
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SECURITY_ERROR");
    }

    [Fact]
    public void Redact_CdataTerminatorInReplacement_ShouldFail()
    {
        var content = "Ahmet Yılmaz";
        var udfBytes = CreateRealisticUdf(content);
        var plan = new RedactionPlan
        {
            DocumentId = "cdata",
            Format = DF.Udf,
            Operations = new[]
            {
                new RedactionOperation { DetectionId = "1", DetectionType = DetectionType.FullName, TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" }, State = RedactionOperationState.Pending, ReplacementText = "]]>" }
            }
        };
        var result = _redactor.Redact(udfBytes, plan, new RenderOptions());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SECURITY_ERROR");
    }

    // Helpers
    private byte[] CreateMinimalUdf(string content)
    {
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

    private byte[] CreateRealisticUdf(string content)
    {
        // Build template with single paragraph, single element covering all
        var escaped = System.Security.SecurityElement.Escape(content);
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?><template format_id=""1.8""><content><![CDATA[{content}]]></content><properties><pageFormat mediaSizeName=""1""/></properties><elements><paragraph><content startOffset=""0"" length=""{content.Length}""/></paragraph></elements><styles><style name=""default""/></styles></template>";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("content.xml");
            using var s = entry.Open();
            using var w = new StreamWriter(s, new UTF8Encoding(false));
            w.Write(xml);
        }
        return ms.ToArray();
    }

    private byte[] CreateUdfWithMultiElement(string content)
    {
        // Split into word-based elements within single paragraph to force multi-element
        var words = content.Split(' ');
        var sb = new StringBuilder();
        sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?><template format_id=""1.8""><content><![CDATA[");
        sb.Append(content);
        sb.Append(@"]]></content><properties><pageFormat mediaSizeName=""1""/></properties><elements><paragraph>");
        int off = 0;
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            sb.Append($@"<content startOffset=""{off}"" length=""{w.Length}""/>");
            off += w.Length;
            if (i < words.Length - 1)
            {
                sb.Append($@"<space startOffset=""{off}"" length=""1""/>");
                off += 1;
            }
        }
        sb.Append(@"</paragraph></elements><styles><style name=""default""/></styles></template>");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("content.xml");
            using var s = entry.Open();
            using var w = new StreamWriter(s, new UTF8Encoding(false));
            w.Write(sb.ToString());
        }
        return ms.ToArray();
    }

    private byte[] CreateCrossParagraphUdf()
    {
        var content = "PARA1_LINE1\nPARA2_LINE1";
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?><template format_id=""1.8""><content><![CDATA[{content}]]></content><properties><pageFormat mediaSizeName=""1""/></properties><elements><paragraph><content startOffset=""0"" length=""11""/></paragraph><paragraph><content startOffset=""12"" length=""11""/></paragraph></elements><styles><style name=""default""/></styles></template>";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("content.xml");
            using var s = entry.Open();
            using var w = new StreamWriter(s, new UTF8Encoding(false));
            w.Write(xml);
        }
        return ms.ToArray();
    }

    private byte[] CreateMalformedUdf()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("content.xml");
            using var s = entry.Open();
            using var w = new StreamWriter(s, Encoding.UTF8);
            w.Write("<template><content><![CDATA[unclosed");
        }
        return ms.ToArray();
    }

    private byte[] CreateUdfMissingContentXml()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("other.xml");
            using var s = entry.Open();
            using var w = new StreamWriter(s, Encoding.UTF8);
            w.Write("<root/>");
        }
        return ms.ToArray();
    }

    private byte[] CreateUdfWithUnsafeEntry(string entryName)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var c = zip.CreateEntry("content.xml");
            using (var s = c.Open()) using (var w = new StreamWriter(s, Encoding.UTF8)) w.Write(@"<template><content><![CDATA[test]]></content><properties/><elements><paragraph><content startOffset=""0"" length=""4""/></paragraph></elements><styles/></template>");
            var evil = zip.CreateEntry(entryName);
            using (var s = evil.Open()) using (var w = new StreamWriter(s, Encoding.UTF8)) w.Write("evil");
        }
        return ms.ToArray();
    }

    private static string ExtractContentText(byte[] zipBytes)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("content.xml")!;
        using var s = entry.Open();
        using var sr = new StreamReader(s, Encoding.UTF8);
        var xml = sr.ReadToEnd();
        var xdoc = XDocument.Parse(xml);
        XElement? contentEl = null;
        if (string.Equals(xdoc.Root!.Name.LocalName, "content", StringComparison.OrdinalIgnoreCase))
            contentEl = xdoc.Root;
        else
            contentEl = xdoc.Root!.Element("content") ?? xdoc.Root!.Descendants().FirstOrDefault(e => e.Name.LocalName == "content");
        contentEl ??= xdoc.Root;
        // If minimal <content><body> case, get body
        if (contentEl.Element("body") != null) return contentEl.Element("body")!.Value;
        return contentEl.Value;
    }

    private static string ExtractContentXml(byte[] zipBytes)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("content.xml")!;
        using var s = entry.Open();
        using var sr = new StreamReader(s, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static void ValidateOffsets(string xml, string content)
    {
        var xdoc = XDocument.Parse(xml);
        var elementsEl = xdoc.Root!.Element("elements");
        if (elementsEl == null) return;
        foreach (var child in elementsEl.Descendants().Where(e => e.Attribute("startOffset") != null))
        {
            int so = int.Parse(child.Attribute("startOffset")!.Value);
            int len = int.Parse((child.Attribute("length") ?? child.Attribute("len"))!.Value);
            so.Should().BeGreaterThanOrEqualTo(0);
            len.Should().BeGreaterThanOrEqualTo(0);
            (so + len).Should().BeLessThanOrEqualTo(content.Length + 2);
            if (len > 0) content.Substring(so, Math.Min(len, content.Length - so)).Should().NotBeNull();
        }
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
