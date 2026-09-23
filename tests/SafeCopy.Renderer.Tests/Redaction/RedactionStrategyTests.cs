using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.Renderer.Redaction;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Xunit;

namespace SafeCopy.Renderer.Tests.Redaction;

public class RedactionStrategyTests
{
    [Theory]
    [InlineData(DetectionType.TcKimlikNo, "[TC_KIMLIK_NO]")]
    [InlineData(DetectionType.Phone, "[PHONE]")]
    [InlineData(DetectionType.Email, "[EMAIL]")]
    [InlineData(DetectionType.Iban, "[IBAN]")]
    [InlineData(DetectionType.FullName, "[NAME]")]
    [InlineData(DetectionType.Address, "[ADDRESS]")]
    [InlineData(DetectionType.Date, "[DATE]")]
    [InlineData(DetectionType.TesisatNo, "[TESISAT_NO]")]
    [InlineData(DetectionType.AboneNo, "[ABONE_NO]")]
    [InlineData(DetectionType.SayacNo, "[SAYAC_NO]")]
    [InlineData(DetectionType.MusteriNo, "[MUSTERI_NO]")]
    [InlineData(DetectionType.DosyaNo, "[DOSYA_NO]")]
    [InlineData(DetectionType.DavaNo, "[DAVA_NO]")]
    [InlineData(DetectionType.CreditCard, "[CREDIT_CARD]")]
    [InlineData(DetectionType.TaxId, "[TAX_ID]")]
    [InlineData(DetectionType.PassportNo, "[PASSPORT]")]
    [InlineData(DetectionType.LicensePlate, "[PLATE]")]
    public void DefaultStrategy_TypeLabel_MapsDetectionTypeToPlaceholder(DetectionType type, string expected)
    {
        var strategy = new DefaultRedactionStrategy();
        var options = new RenderOptions { UseTypePlaceholder = true };
        var result = strategy.GetReplacementText(type, options);
        result.Should().Be(expected);
    }

    [Fact]
    public void DefaultStrategy_TypeLabelPlaceholderDisabled_ReturnsGenericPlaceholder()
    {
        var strategy = new DefaultRedactionStrategy();
        var options = new RenderOptions { UseTypePlaceholder = false, PlaceholderText = "[REDACTED]" };
        strategy.GetReplacementText(DetectionType.TcKimlikNo, options).Should().Be("[REDACTED]");
        strategy.GetReplacementText(DetectionType.Email, options).Should().Be("[REDACTED]");
    }

    [Fact]
    public void DefaultStrategy_CustomPlaceholderOverrides()
    {
        var strategy = new DefaultRedactionStrategy();
        var options = new RenderOptions
        {
            UseTypePlaceholder = true,
            TypePlaceholders = new Dictionary<DetectionType, string> { { DetectionType.Email, "[E-POSTA]" } }
        };
        strategy.GetReplacementText(DetectionType.Email, options).Should().Be("[E-POSTA]");
        // Other types fall back to global placeholder if not in dictionary
        strategy.GetReplacementText(DetectionType.Phone, options).Should().Be(options.PlaceholderText);
    }

    [Fact]
    public void DefaultStrategy_Type_IsTypeLabel()
    {
        var strategy = new DefaultRedactionStrategy();
        strategy.Type.Should().Be(RedactionStrategy.TypeLabel);
    }

    [Fact]
    public void FullRedactionStrategy_ReturnsEmptyString()
    {
        var strategy = new FullRedactionStrategy();
        var options = new RenderOptions();
        strategy.GetReplacementText(DetectionType.TcKimlikNo, options).Should().BeEmpty();
        strategy.GetReplacementText(DetectionType.Email, options).Should().BeEmpty();
        strategy.Type.Should().Be(RedactionStrategy.FullRedaction);
    }

    [Fact]
    public void PlaceholderStrategy_ReturnsPlaceholderText()
    {
        var strategy = new PlaceholderStrategy();
        var options = new RenderOptions { PlaceholderText = "***" };
        strategy.GetReplacementText(DetectionType.Phone, options).Should().Be("***");
        strategy.GetReplacementText(DetectionType.TcKimlikNo, options).Should().Be("***");
        strategy.Type.Should().Be(RedactionStrategy.Placeholder);
    }

    [Fact]
    public void PartialMaskStrategy_ReturnsPlaceholderText()
    {
        var strategy = new PartialMaskStrategy();
        var options = new RenderOptions { PlaceholderText = "[MASK]" };
        strategy.GetReplacementText(DetectionType.Iban, options).Should().Be("[MASK]");
        strategy.Type.Should().Be(RedactionStrategy.PartialMask);
    }

    [Fact]
    public void Factory_CreatesCorrectStrategy()
    {
        RedactionStrategyFactory.Create(RedactionStrategy.FullRedaction).Should().BeOfType<FullRedactionStrategy>();
        RedactionStrategyFactory.Create(RedactionStrategy.TypeLabel).Should().BeOfType<DefaultRedactionStrategy>();
        RedactionStrategyFactory.Create(RedactionStrategy.Placeholder).Should().BeOfType<PlaceholderStrategy>();
        RedactionStrategyFactory.Create(RedactionStrategy.PartialMask).Should().BeOfType<PartialMaskStrategy>();
        RedactionStrategyFactory.Create(RedactionStrategy.Custom).Should().BeOfType<DefaultRedactionStrategy>();
    }

    [Theory]
    [InlineData(DF.Txt)]
    [InlineData(DF.Docx)]
    [InlineData(DF.Xlsx)]
    [InlineData(DF.Png)]
    [InlineData(DF.Pdf)]
    [InlineData(DF.Udf)]
    public void Strategy_SupportsAllFormatsExceptUnknown(DF format)
    {
        var strategy = new DefaultRedactionStrategy();
        strategy.SupportsFormat(format).Should().BeTrue();
        new FullRedactionStrategy().SupportsFormat(format).Should().BeTrue();
        new PlaceholderStrategy().SupportsFormat(format).Should().BeTrue();
        new PartialMaskStrategy().SupportsFormat(format).Should().BeTrue();
    }

    [Fact]
    public void Strategy_UnknownFormat_NotSupported()
    {
        new DefaultRedactionStrategy().SupportsFormat(DF.Unknown).Should().BeFalse();
    }

    [Fact]
    public void DefaultStrategy_UnknownDetectionType_FallsBackToPlaceholder()
    {
        var strategy = new DefaultRedactionStrategy();
        var options = new RenderOptions { UseTypePlaceholder = true, PlaceholderText = "[REDACTED]" };
        // Custom type not in dictionary
        strategy.GetReplacementText(DetectionType.Custom, options).Should().Be("[REDACTED]");
        strategy.GetReplacementText(DetectionType.Unknown, options).Should().Be("[REDACTED]");
    }

    [Fact]
    public void RedactionPlanner_UsesStrategyPlaceholder_Mapping()
    {
        var strategy = new DefaultRedactionStrategy();
        var planner = new RedactionPlanner(strategy);
        var doc = new Document("test.txt", DF.Txt, new[] { new DocumentPage(100, 100, 1, 72, 72) { Text = "test", PageNumber = 1 } });
        var detection = new Detection { Type = DetectionType.TcKimlikNo, Value = "11111111111", TextSpan = new TextSpan { StartIndex = 0, Length = 11, Text = "11111111111" }, Confidence = 0.9, PageNumber = 1 };
        var options = new RenderOptions { UseTypePlaceholder = true };
        var result = planner.CreatePlan(doc, new[] { detection }, options);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations[0].ReplacementText.Should().Be("[TC_KIMLIK_NO]");
        result.Value.Operations[0].Strategy.Should().Be(RedactionStrategy.TypeLabel);
    }

    // Password/Secret PartialMask must be fully hidden - regression tests
    [Theory]
    [InlineData("abc123_Se&")]
    [InlineData("P@ssw0rd!2024")]
    [InlineData("MySecret123#90C")]
    public void PartialMaskingPolicy_Secret_FullyHidden(string secret)
    {
        var strategy = new PartialMaskStrategy();
        var options = new RenderOptions();
        var masked = strategy.GetReplacementText(DetectionType.Secret, secret, options);
        masked.Should().Be("[SECRET]");
        masked.Should().NotContain(secret);
        foreach (var sub in GetSubstrings(secret, 3))
        {
            if (sub.Any(char.IsLetterOrDigit))
                masked.Should().NotContain(sub);
        }
    }

    [Fact]
    public void PartialMaskingPolicy_Secret_FullMaskPlaceholder()
    {
        var strategy = new DefaultRedactionStrategy();
        var options = new RenderOptions { UseTypePlaceholder = true };
        strategy.GetReplacementText(DetectionType.Secret, options).Should().Be("[SECRET]");
    }

    [Fact]
    public void PartialMaskingPolicy_Address_PartialStillFullPlaceholder()
    {
        var strategy = new PartialMaskStrategy();
        var options = new RenderOptions();
        var masked = strategy.GetReplacementText(DetectionType.Address, "Yıldız Mah. Çınar Sokak No: 117 Daire: 12, Trabzon", options);
        masked.Should().Be("[ADDRESS]");
    }

    [Fact]
    public void PartialMaskingPolicy_Email_PreservesDomain()
    {
        var strategy = new PartialMaskStrategy();
        var options = new RenderOptions();
        var masked = strategy.GetReplacementText(DetectionType.Email, "ahmet.yilmaz@example.com", options);
        masked.Should().Contain("@example.com");
        masked.Should().NotContain("ahmet.yilmaz");
        masked.Should().Be("a***********@example.com");
    }

    [Fact]
    public void PartialMaskingPolicy_Phone_PreservesFormatting()
    {
        var strategy = new PartialMaskStrategy();
        var options = new RenderOptions();
        var masked = strategy.GetReplacementText(DetectionType.Phone, "0532 123 45 67", options);
        masked.Should().Contain("0532");
        masked.Should().Contain("67");
        masked.Should().Contain("*");
    }

    private static IEnumerable<string> GetSubstrings(string s, int minLen)
    {
        for (int i = 0; i <= s.Length - minLen; i++)
            for (int len = minLen; len <= s.Length - i; len++)
                yield return s.Substring(i, len);
    }
}
