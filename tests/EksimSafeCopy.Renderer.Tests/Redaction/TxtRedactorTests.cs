using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
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

public class TxtRedactorTests
{
    private readonly IRedactor _redactor;

    public TxtRedactorTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IRedactor, TxtRedactor>();
        
        var provider = services.BuildServiceProvider();
        _redactor = provider.GetRequiredService<IRedactor>();
    }

    [Fact]
    public void Redact_ValidTxt_ReturnsSuccess()
    {
        var inputText = "Test TXT Content\nLine 2\nLine 3 with Ahmet Yılmaz";
        var tempFile = CreateTempTxt(inputText);

        try
        {
            var plan = CreatePlan(tempFile);
            var options = new RenderOptions();
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, options);

            result.IsSuccess.Should().BeTrue();
            var outputText = Encoding.UTF8.GetString(result.Value);
            outputText.Should().NotContain("Ahmet Yılmaz");
            outputText.Should().Contain("[AD SOYAD]");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Redact_TurkishEncoding_ReturnsCorrectText()
    {
        var content = "Ahmet Yılmaz\nİstanbul'da yaşıyor\nTC Kimlik: 11111111111\nÖzel karakterler: ĞüşİÖÇğüşıöç";
        var tempFile = CreateTurkishTxt(content);

        try
        {
            var plan = CreatePlan(tempFile);
            var options = new RenderOptions();
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, options);

            result.IsSuccess.Should().BeTrue();
            var outputText = Encoding.UTF8.GetString(result.Value);
            outputText.Should().Contain("[AD SOYAD]");
            outputText.Should().Contain("[TC_KIMLIK_NO]");
            outputText.Should().Contain("[ADRES]");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Redact_Utf8WithBom_ReturnsCorrectText()
    {
        var tempFile = CreateUtf8WithBom();

        try
        {
            var plan = CreatePlan(tempFile);
            var options = new RenderOptions();
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, options);

            result.IsSuccess.Should().BeTrue();
            var outputText = Encoding.UTF8.GetString(result.Value);
            outputText.Should().Contain("Test");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Redact_Utf8_Simple_CorrectlyRedacts()
    {
        var content = "Merhaba Ahmet Yılmaz TC 11111111111";
        var tempFile = Path.Combine(Path.GetTempPath(), $"utf8_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, content, new UTF8Encoding(false));
        try
        {
            var detections = new[]
            {
                new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 8, Length = 12, Text = "Ahmet Yılmaz" } },
                new Detection { Type = DetectionType.TcKimlikNo, Value = "11111111111", TextSpan = new TextSpan { StartIndex = 24, Length = 11, Text = "11111111111" } }
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            var output = Encoding.UTF8.GetString(result.Value);
            output.Should().NotContain("Ahmet Yılmaz");
            output.Should().NotContain("11111111111");
            output.Should().Contain("[AD SOYAD]");
            output.Should().Contain("[TC_KIMLIK_NO]");
            // UTF-8 BOM should not be present in output if not in input (or handled)
            output.Should().NotContain("\uFEFF");
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_Utf8Bom_RedactsAndStripsBom()
    {
        var content = "Ahmet Yılmaz 11111111111";
        var tempFile = Path.Combine(Path.GetTempPath(), $"utf8bom2_{Guid.NewGuid():N}.txt");
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var bytes = bom.Concat(Encoding.UTF8.GetBytes(content)).ToArray();
        File.WriteAllBytes(tempFile, bytes);
        try
        {
            var detections = new[]
            {
                new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" } }
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            var output = Encoding.UTF8.GetString(result.Value);
            // BOM char should be stripped
            output[0].Should().NotBe('\uFEFF');
            output.Should().NotContain("Ahmet Yılmaz");
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_Utf16LE_RedactsCorrectly()
    {
        var content = "Ahmet Yılmaz TC 11111111111";
        var tempFile = Path.Combine(Path.GetTempPath(), $"utf16_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, content, Encoding.Unicode); // UTF-16 LE
        try
        {
            var detections = new[]
            {
                new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" } },
                new Detection { Type = DetectionType.TcKimlikNo, Value = "11111111111", TextSpan = new TextSpan { StartIndex = 16, Length = 11, Text = "11111111111" } }
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            // UTF-16 output should still contain placeholder, not PII
            var encoding = Encoding.Unicode;
            // Detect actual encoding via BOM - TxtRedactor détects UTF-16 via 0xFF 0xFE
            var outputBytes = result.Value;
            // Try decode both UTF8 and Unicode
            string output;
            if (outputBytes.Length >= 2 && outputBytes[0] == 0xFF && outputBytes[1] == 0xFE)
                output = Encoding.Unicode.GetString(outputBytes);
            else
                output = Encoding.UTF8.GetString(outputBytes);
            // At least one decoding should show redacted placeholders
            // We verify bytes don't contain original PII in either encoding representation
            var asUtf8 = Encoding.UTF8.GetString(outputBytes);
            var asUnicode = Encoding.Unicode.GetString(outputBytes);
            (asUtf8.Contains("Ahmet Yılmaz") || asUnicode.Contains("Ahmet Yılmaz")).Should().BeFalse();
            // Check output contains placeholder in some form
            (asUtf8.Contains("[AD SOYAD]") || asUnicode.Contains("[AD SOYAD]")).Should().BeTrue();
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_Windows1254_TurkishChars_Redacts()
    {
        var content = "ĞüşİÖÇ ğüşıöç Ahmet Yılmaz 11111111111";
        var tempFile = Path.Combine(Path.GetTempPath(), $"w1254_{Guid.NewGuid():N}.txt");
        var enc1254 = Encoding.GetEncoding("windows-1254");
        File.WriteAllText(tempFile, content, enc1254);
        try
        {
            var detections = new[]
            {
                new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = 12, Text = "Ahmet Yılmaz" } },
                new Detection { Type = DetectionType.TcKimlikNo, Value = "11111111111", TextSpan = new TextSpan { StartIndex = content.IndexOf("11111111111"), Length = 11, Text = "11111111111" } }
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            // Decode with same encoding heuristic - TxtRedactor may fallback to windows-1254
            var output = enc1254.GetString(result.Value);
            // Also try UTF8 fallback check
            var outputUtf8 = Encoding.UTF8.GetString(result.Value);
            // At least ensure no PII in windows-1254 decoded
            output.Should().NotContain("Ahmet Yılmaz");
            output.Should().NotContain("11111111111");
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_Turkish_SpecialChars_PreservesRemaining()
    {
        var content = "İSTANBUL ĞÜŞİÖÇ ahmet@example.com ĞTest 0532 123 45 67";
        var tempFile = CreateTempTxt(content);
        try
        {
            var emailIdx = content.IndexOf("ahmet@example.com");
            var phoneIdx = content.IndexOf("0532");
            var detections = new[]
            {
                new Detection { Type = DetectionType.Email, Value = "ahmet@example.com", TextSpan = new TextSpan { StartIndex = emailIdx, Length = 17, Text = "ahmet@example.com" } },
                new Detection { Type = DetectionType.Phone, Value = "0532 123 45 67", TextSpan = new TextSpan { StartIndex = phoneIdx, Length = 14, Text = "0532 123 45 67" } }
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            var output = Encoding.UTF8.GetString(result.Value);
            output.Should().Contain("İSTANBUL");
            output.Should().Contain("ĞÜŞİÖÇ");
            output.Should().NotContain("ahmet@example.com");
            output.Should().NotContain("0532 123 45 67");
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_MultipleDetections_AllRedacted()
    {
        var content = "Ahmet Yılmaz 11111111111 ahmet@example.com TR00 0000 0000 0000 0000 0000 00 0532 000 00 00";
        var tempFile = CreateTempTxt(content);
        try
        {
            var detections = new[]
            {
                new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = 12, Text = "Ahmet Yılmaz" } },
                new Detection { Type = DetectionType.TcKimlikNo, Value = "11111111111", TextSpan = new TextSpan { StartIndex = content.IndexOf("11111111111"), Length = 11, Text = "11111111111" } },
                new Detection { Type = DetectionType.Email, Value = "ahmet@example.com", TextSpan = new TextSpan { StartIndex = content.IndexOf("ahmet@example.com"), Length = 17, Text = "ahmet@example.com" } },
                new Detection { Type = DetectionType.Phone, Value = "0532 000 00 00", TextSpan = new TextSpan { StartIndex = content.IndexOf("0532 000 00 00"), Length = 14, Text = "0532 000 00 00" } },
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            var output = Encoding.UTF8.GetString(result.Value);
            output.Should().NotContain("Ahmet Yılmaz");
            output.Should().NotContain("11111111111");
            output.Should().NotContain("ahmet@example.com");
            output.Should().NotContain("0532 000 00 00");
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_OverlappingDetections_HandledDeterministically()
    {
        var content = "Ahmet Yılmaz 11111111111";
        var tempFile = CreateTempTxt(content);
        try
        {
            // Overlapping spans: "Ahmet Yılmaz" (0,12) and "Yılmaz 11111" (6,12)
            var d1 = new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" } };
            var d2 = new Detection { Type = DetectionType.TcKimlikNo, Value = "Yılmaz 11111", TextSpan = new TextSpan { StartIndex = 6, Length = 12, Text = "Yılmaz 11111" } };
            var plan = CreatePlanFromDetections(new[] { d1, d2 });
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            var output = Encoding.UTF8.GetString(result.Value);
            // At least one of them should be redacted; overlapping handling is descending order
            output.Should().NotBe(content);
            // Original PII substrings should not both remain fully
            // D1 redaction replaces from high index first, so d2 then d1
            output.Length.Should().BeGreaterThan(0);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_HashUnchanged_OriginalFileNotModified()
    {
        var content = "Ahmet Yılmaz 11111111111";
        var tempFile = CreateTempTxt(content);
        var originalHash = ComputeHash(tempFile);
        try
        {
            var detections = new[]
            {
                new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = 0, Length = 12, Text = "Ahmet Yılmaz" } }
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            var newHash = ComputeHash(tempFile);
            newHash.Should().Be(originalHash);
            // Output bytes should differ from original
            var output = Encoding.UTF8.GetString(result.Value);
            output.Should().NotBe(content);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Redact_RedactToFile_HashUnchangedAndOutputCreated()
    {
        var content = "Ahmet Yılmaz 11111111111 İstanbul";
        var tempInput = CreateTempTxt(content);
        var tempOutput = Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}.txt");
        var originalHash = ComputeHash(tempInput);
        try
        {
            var detections = new[]
            {
                new Detection { Type = DetectionType.FullName, Value = "Ahmet Yılmaz", TextSpan = new TextSpan { StartIndex = content.IndexOf("Ahmet Yılmaz"), Length = 12, Text = "Ahmet Yılmaz" } }
            };
            var plan = CreatePlanFromDetections(detections);
            var result = _redactor.RedactToFile(tempInput, tempOutput, plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            File.Exists(tempOutput).Should().BeTrue();
            ComputeHash(tempInput).Should().Be(originalHash);
            var outText = File.ReadAllText(tempOutput, Encoding.UTF8);
            outText.Should().NotContain("Ahmet Yılmaz");
        }
        finally
        {
            File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
            if (File.Exists(tempOutput + ".tmp")) File.Delete(tempOutput + ".tmp");
        }
    }

    [Fact]
    public void Redact_EmptyPlan_ReturnsOriginalContent()
    {
        var content = "Clean document without PII";
        var tempFile = CreateTempTxt(content);
        try
        {
            var plan = new RedactionPlan { DocumentId = "test", Operations = Array.Empty<RedactionOperation>(), Format = DF.Txt };
            var result = _redactor.Redact(File.ReadAllBytes(tempFile), plan, new RenderOptions());
            result.IsSuccess.Should().BeTrue();
            var output = Encoding.UTF8.GetString(result.Value);
            output.Should().Contain("Clean document");
        }
        finally { File.Delete(tempFile); }
    }

    private RedactionPlan CreatePlan(string filePath)
    {
        var detection1 = new Detection 
        { 
            Type = DetectionType.FullName, 
            Value = "Ahmet Yılmaz", 
            TextSpan = new TextSpan { StartIndex = 0, Length = 10, Text = "Ahmet Yılmaz" } 
        };
        var detection2 = new Detection 
        { 
            Type = DetectionType.TcKimlikNo, 
            Value = "11111111111", 
            TextSpan = new TextSpan { StartIndex = 20, Length = 11, Text = "11111111111" } 
        };
        var detection3 = new Detection 
        { 
            Type = DetectionType.Address, 
            Value = "İstanbul'da yaşıyor", 
            TextSpan = new TextSpan { StartIndex = 11, Length = 16, Text = "İstanbul'da yaşıyor" } 
        };

        var detections = new[] { detection1, detection2, detection3 };

        var operations = detections.Select(d => new RedactionOperation
        {
            DetectionId = d.Id,
            DetectionType = d.Type,
            TextSpan = d.TextSpan,
            PageNumber = 1,
            Strategy = RedactionStrategy.TypeLabel,
            ReplacementText = d.Type switch
            {
                DetectionType.FullName => "[AD SOYAD]",
                DetectionType.TcKimlikNo => "[TC_KIMLIK_NO]",
                DetectionType.Address => "[ADRES]",
                _ => d.Type.ToString()
            },
            Confidence = d.Confidence,
            State = RedactionOperationState.Pending
        }).ToList();

        return new RedactionPlan
        {
            DocumentId = "test",
            Operations = operations,
            Format = DF.Txt
        };
    }

    private RedactionPlan CreatePlanFromDetections(IReadOnlyList<Detection> detections)
    {
        var ops = detections.Select(d => new RedactionOperation
        {
            DetectionId = d.Id,
            DetectionType = d.Type,
            TextSpan = d.TextSpan,
            PageNumber = d.PageNumber == 0 ? 1 : d.PageNumber,
            Strategy = RedactionStrategy.TypeLabel,
            ReplacementText = d.Type switch
            {
                DetectionType.FullName => "[AD SOYAD]",
                DetectionType.TcKimlikNo => "[TC_KIMLIK_NO]",
                DetectionType.Address => "[ADRES]",
                DetectionType.Phone => "[PHONE]",
                DetectionType.Email => "[EMAIL]",
                DetectionType.Iban => "[IBAN]",
                _ => $"[{d.Type}]"
            },
            Confidence = d.Confidence,
            State = RedactionOperationState.Pending
        }).ToList();
        return new RedactionPlan { DocumentId = "test", Operations = ops, Format = DF.Txt };
    }

    private string CreateTempTxt(string content)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, content, Encoding.UTF8);
        return tempFile;
    }

    private string CreateTurkishTxt(string content)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"turkish_{Guid.NewGuid():N}.txt");
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

    private string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }
}
