using SafeCopy.Core.Models;
using FluentAssertions;
using Xunit;

namespace SafeCopy.Core.Tests.Models;

public class DetectionTests
{
    [Fact]
    public void Constructor_With_Valid_Values_Creates_Detection()
    {
        var detection = new Detection
        {
            Type = DetectionType.TcKimlikNo,
            Value = "11111111111",
            Context = "TC Kimlik No: 11111111111",
            Confidence = 0.95,
            Location = new BoundingBox(100, 200, 50, 20, 800, 600),
            PageNumber = 1,
            DetectionSource = DetectionSource.NativeText,
            State = DetectionState.Detected
        };
        
        detection.Type.Should().Be(DetectionType.TcKimlikNo);
        detection.Value.Should().Be("11111111111");
        detection.Confidence.Should().Be(0.95);
        detection.PageNumber.Should().Be(1);
        detection.DetectionSource.Should().Be(DetectionSource.NativeText);
        detection.State.Should().Be(DetectionState.Detected);
    }

[Fact]
    public void Constructor_Confidence_Below_Zero_Throws()
    {
        var act = () => new Detection(-0.1);
        
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    
    [Fact]
    public void Constructor_Confidence_Above_One_Throws()
    {
        var act = () => new Detection(1.1);
        
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    
    [Fact]
    public void Constructor_Confidence_At_Boundaries_Is_Valid()
    {
        var d1 = new Detection(0);
        var d2 = new Detection(1);
        
        d1.Confidence.Should().Be(0);
        d2.Confidence.Should().Be(1);
    }

    [Fact]
    public void Default_Values_Are_Valid()
    {
        var detection = new Detection();
        
        detection.Id.Should().NotBeEmpty();
        detection.Type.Should().Be(DetectionType.Unknown);
        detection.Value.Should().BeEmpty();
        detection.Confidence.Should().Be(0);
        detection.ConfidenceLevel.Should().Be(ConfidenceLevel.Low);
        detection.Location.Should().Be(BoundingBox.Empty);
        detection.PageNumber.Should().Be(0);
        detection.DetectionSource.Should().Be(DetectionSource.NativeText);
        detection.State.Should().Be(DetectionState.Detected);
        detection.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Properties_Can_Be_Added()
    {
        var detection = new Detection
        {
            Properties = new Dictionary<string, object>
            {
                ["validator"] = "TurkishTcKimlikValidator",
                ["checksum"] = true
            }
        };
        
        detection.Properties.Should().HaveCount(2);
        detection.Properties["validator"].Should().Be("TurkishTcKimlikValidator");
        detection.Properties["checksum"].Should().Be(true);
    }

    [Fact]
    public void State_Can_Be_Changed()
    {
        var detection = new Detection { State = DetectionState.Detected };
        
        detection.State = DetectionState.Selected;
        detection.State.Should().Be(DetectionState.Selected);
        
        detection.State = DetectionState.Masked;
        detection.State.Should().Be(DetectionState.Masked);
    }

    [Fact]
    public void TextSpan_Can_Be_Associated()
    {
        var textSpan = new TextSpan { StartIndex = 10, Length = 11, Text = "11111111111" };
        var detection = new Detection { TextSpan = textSpan };
        
        detection.TextSpan.Should().BeSameAs(textSpan);
        detection.TextSpan!.Text.Should().Be("11111111111");
    }
}

public class DetectionTypeTests
{
    [Fact]
    public void All_Critical_Types_Are_Marked()
    {
        DetectionType.TcKimlikNo.IsCritical().Should().BeTrue();
        DetectionType.Iban.IsCritical().Should().BeTrue();
        DetectionType.TaxId.IsCritical().Should().BeTrue();
        DetectionType.PassportNo.IsCritical().Should().BeTrue();
        DetectionType.CreditCard.IsCritical().Should().BeTrue();
    }

    [Fact]
    public void Non_Critical_Types_Are_Not_Marked()
    {
        DetectionType.Phone.IsCritical().Should().BeFalse();
        DetectionType.Email.IsCritical().Should().BeFalse();
        DetectionType.FullName.IsCritical().Should().BeFalse();
        DetectionType.Address.IsCritical().Should().BeFalse();
        DetectionType.Date.IsCritical().Should().BeFalse();
        DetectionType.Custom.IsCritical().Should().BeFalse();
    }

    [Fact]
    public void All_Required_Types_Are_Present()
    {
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.TcKimlikNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.Phone);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.Email);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.Iban);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.TaxId);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.PassportNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.Date);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.LicensePlate);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.TesisatNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.AboneNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.SayacNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.MusteriNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.DosyaNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.DavaNo);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.FullName);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.Address);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.CreditCard);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.IpAddress);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.Url);
        Enum.GetValues<DetectionType>().Should().Contain(DetectionType.Custom);
    }
}

public class ConfidenceLevelTests
{
    [Fact]
    public void Levels_Have_Correct_Order()
    {
        ((int)ConfidenceLevel.Low).Should().Be(0);
        ((int)ConfidenceLevel.Medium).Should().Be(1);
        ((int)ConfidenceLevel.High).Should().Be(2);
        ((int)ConfidenceLevel.Critical).Should().Be(3);
    }
}

public class DetectionSourceTests
{
    [Fact]
    public void All_Sources_Are_Present()
    {
        Enum.GetValues<DetectionSource>().Should().Contain(DetectionSource.NativeText);
        Enum.GetValues<DetectionSource>().Should().Contain(DetectionSource.OcrText);
        Enum.GetValues<DetectionSource>().Should().Contain(DetectionSource.Metadata);
        Enum.GetValues<DetectionSource>().Should().Contain(DetectionSource.EmbeddedObject);
        Enum.GetValues<DetectionSource>().Should().Contain(DetectionSource.HiddenContent);
        Enum.GetValues<DetectionSource>().Should().Contain(DetectionSource.Manual);
    }
}

public class DetectionStateTests
{
    [Fact]
    public void All_States_Are_Present()
    {
        Enum.GetValues<DetectionState>().Should().Contain(DetectionState.Detected);
        Enum.GetValues<DetectionState>().Should().Contain(DetectionState.Selected);
        Enum.GetValues<DetectionState>().Should().Contain(DetectionState.Deselected);
        Enum.GetValues<DetectionState>().Should().Contain(DetectionState.Masked);
        Enum.GetValues<DetectionState>().Should().Contain(DetectionState.FalsePositive);
    }
}

public class RenderOptionsTests
{
    [Fact]
    public void Default_Values_Are_Valid()
    {
        var options = new RenderOptions();
        
        options.Mode.Should().Be(MaskingMode.FullRedaction);
        options.PlaceholderText.Should().Be("[REDACTED]");
        options.UseTypePlaceholder.Should().BeTrue();
        options.MaskRepeatedValues.Should().BeTrue();
        options.SanitizeMetadata.Should().BeTrue();
        options.RemoveHiddenContent.Should().BeTrue();
        options.TypePlaceholders.Should().NotBeEmpty();
    }

    [Fact]
    public void TypePlaceholders_Contains_All_Critical_Types()
    {
        var options = new RenderOptions();
        
        options.TypePlaceholders.Should().ContainKey(DetectionType.TcKimlikNo);
        options.TypePlaceholders.Should().ContainKey(DetectionType.Iban);
        options.TypePlaceholders.Should().ContainKey(DetectionType.TaxId);
        options.TypePlaceholders.Should().ContainKey(DetectionType.PassportNo);
        options.TypePlaceholders.Should().ContainKey(DetectionType.CreditCard);
    }
}

public class VerificationResultTests
{
    [Fact]
    public void Default_Values_Are_Valid()
    {
        var result = new VerificationResult();
        
        result.Passed.Should().BeFalse();
        result.ResidualDetections.Should().BeEmpty();
        result.MetadataIssues.Should().BeEmpty();
        result.HiddenContentIssues.Should().BeEmpty();
        result.ScanDuration.Should().Be(TimeSpan.Zero);
        result.VerifiedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        result.CriticalResidualCount.Should().Be(0);
        result.TotalResidualCount.Should().Be(0);
    }

    [Fact]
    public void Passed_Result_Has_No_Residuals()
    {
        var result = new VerificationResult
        {
            Passed = true,
            ResidualDetections = Array.Empty<ResidualDetection>()
        };
        
        result.Passed.Should().BeTrue();
        result.CriticalResidualCount.Should().Be(0);
        result.TotalResidualCount.Should().Be(0);
    }

    [Fact]
    public void Failed_Result_Counts_Critical_Residuals()
    {
        var residuals = new List<ResidualDetection>
        {
            new() { Type = DetectionType.TcKimlikNo, Confidence = 0.9 },
            new() { Type = DetectionType.Phone, Confidence = 0.8 },
            new() { Type = DetectionType.Email, Confidence = 0.7 }
        };
        
        var result = new VerificationResult
        {
            Passed = false,
            ResidualDetections = residuals
        };
        
        result.CriticalResidualCount.Should().Be(1);
        result.TotalResidualCount.Should().Be(3);
    }

    [Fact]
    public void Metadata_And_Hidden_Content_Issues_Are_Tracked()
    {
        var result = new VerificationResult
        {
            Passed = false,
            MetadataIssues = new List<string> { "Author metadata not removed" },
            HiddenContentIssues = new List<string> { "Hidden text in header" }
        };
        
        result.MetadataIssues.Should().HaveCount(1);
        result.HiddenContentIssues.Should().HaveCount(1);
    }
}

public class ResidualDetectionTests
{
    [Fact]
    public void IsCritical_Delegates_To_DetectionType()
    {
        var critical = new ResidualDetection { Type = DetectionType.TcKimlikNo };
        var nonCritical = new ResidualDetection { Type = DetectionType.Phone };
        
        critical.IsCritical.Should().BeTrue();
        nonCritical.IsCritical.Should().BeFalse();
    }
}