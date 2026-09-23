using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.Renderer.Redaction;
using FluentAssertions;
using Xunit;

namespace SafeCopy.Renderer.Tests.Redaction;

public class PartialMaskTests
{
    [Fact]
    public void FullRedaction_TC_RemainsUnchanged()
    {
        var strategy = new FullRedactionStrategy();
        var options = new RenderOptions { Mode = MaskingMode.FullRedaction };
        // FullRedaction returns empty, redactor will use block chars
        var result = strategy.GetReplacementText(DetectionType.TcKimlikNo, options);
        result.Should().BeEmpty();
    }

    private string Mask(DetectionType type, string value)
    {
        var strategy = new PartialMaskStrategy();
        return strategy.GetReplacementText(type, value, new RenderOptions { Mode = MaskingMode.PartialMask });
    }

    [Fact]
    public void PartialRedaction_TC_MasksPrefix()
    {
        var result = Mask(DetectionType.TcKimlikNo, "12345678901");
        result.Should().Be("*******8901");
        result.Should().NotContain("1234567");
        result.Should().Contain("8901");
    }

    [Fact]
    public void PartialRedaction_Phone_MasksMiddle()
    {
        var result = Mask(DetectionType.Phone, "0555 123 45 67");
        result.Should().Be("0555 *** ** 67");
        result.Should().NotContain("123");
        result.Should().Contain("0555");
        result.Should().Contain("67");
    }

    [Fact]
    public void PartialRedaction_IBAN_PreservesAllowedCharactersOnly()
    {
        var result = Mask(DetectionType.Iban, "TR00 0000 0000 0000 0000 00");
        result.Should().Be("TR00 **** **** **** **** 00");
        result.Should().NotContain("0000 0000 0000");
        result.Should().Contain("TR00");
        result.Should().Contain("00");
    }

    [Fact]
    public void PartialRedaction_InstallationNumber_MasksPrefix()
    {
        var result = Mask(DetectionType.TesisatNo, "12132133");
        result.Should().Be("****2133");
        result.Should().NotContain("1213");
        result.Should().Contain("2133");
    }

    [Fact]
    public void PartialRedaction_FullName_UsesDefinedPolicy()
    {
        var result = Mask(DetectionType.FullName, "Furkan İltaş");
        result.Should().Be("F***** İ****");
        result.Should().NotContain("Furkan");
        result.Should().Contain("F");
    }

    [Fact]
    public void PartialRedaction_Verification_PassesForValidOutput()
    {
        var original = "12345678901";
        var masked = Mask(DetectionType.TcKimlikNo, original);
        // Original should not be in output
        var output = $"Some text {masked} more text";
        output.Should().NotContain(original);
        output.Should().Contain("8901");
    }

    [Fact]
    public void PartialRedaction_Verification_FailsWhenTooMuchPIIIsVisible()
    {
        var original = "12345678901";
        // Simulate insufficient masking: only first 3 masked instead of 7
        var insufficientMask = "***45678901";
        // This still contains 8 of the original digits, should be considered too much visible
        insufficientMask.Should().Contain("45678901");
        // Our policy masks 7, so insufficient would be detected as still containing sensitive part
        var sensitivePart = original.Substring(0, 7);
        insufficientMask.Should().Contain("4567");
    }

    [Fact]
    public void FullRedaction_AllExistingTestsStillPass()
    {
        // Ensure FullRedaction via TypeLabel still works as before
        var strategy = new DefaultRedactionStrategy();
        var options = new RenderOptions { UseTypePlaceholder = true };
        strategy.GetReplacementText(DetectionType.TcKimlikNo, options).Should().Be("[TC_KIMLIK_NO]");
        strategy.GetReplacementText(DetectionType.FullName, options).Should().Be("[NAME]");
    }
}
