using System.IO;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Detectors;
using EksimSafeCopy.Detectors.Detection.Pipeline;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.Detectors.Tests.Detectors;

public class BirthDateDetectorTests
{
    private readonly IDetectionEngine _detectionEngine;

    public BirthDateDetectorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();

        var provider = services.BuildServiceProvider();
        _detectionEngine = provider.GetRequiredService<IDetectionEngine>();
    }

    private Document CreateDocument(string text)
    {
        var page = new DocumentPage
        {
            PageNumber = 1,
            Width = 800,
            Height = 600,
            DpiX = 96,
            DpiY = 96,
            Text = text,
            TextBlocks = new List<TextBlock>
            {
                new TextBlock
                {
                    Text = text,
                    Type = TextBlockType.Paragraph,
                    Direction = TextDirection.LeftToRight,
                    OrderIndex = 0,
                    PageNumber = 1
                }
            }.AsReadOnly()
        };

        return new Document
        {
            Name = "test.txt",
            Format = DF.Txt,
            Pages = new[] { page },
            Metadata = new DocumentMetadata()
        };
    }

    [Fact]
    public void Detect_DotFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "15.06.1990");
    }

    [Fact]
    public void Detect_SlashFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("Doğum T.: 15/06/1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "15/06/1990");
    }

    [Fact]
    public void Detect_DashFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("Doğum: 15-06-1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "15-06-1990");
    }

    [Fact]
    public void Detect_ISOFormatBirthDate_ReturnsDetection()
    {
        var document = CreateDocument("DTarih: 1990-06-15");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "1990-06-15");
    }

    [Fact]
    public void Detect_DateWithoutBirthLabel_NotDetected()
    {
        var document = CreateDocument("Bugünün tarihi: 15.06.2024");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_FutureDate_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.2030");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_InvalidDay_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 32.01.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_InvalidMonth_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 15.13.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_YearBefore1900_NotDetected()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.1899");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date);
    }

    [Fact]
    public void Detect_MultipleBirthDates_AllDetected()
    {
        var document = CreateDocument("Anne: 15.06.1960 Baba: 20.03.1955");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detections = result.Value.Where(d => d.Type == DetectionType.Date).ToList();
        detections.Should().HaveCount(2);
        detections.Select(d => d.Value).Should().Contain("15.06.1960", "20.03.1955");
    }

    [Fact]
    public void Detect_BirthDateFormatProperty_Populated()
    {
        var document = CreateDocument("Doğum Tarihi: 15.06.1990");

        var result = _detectionEngine.Detect(document);

        result.IsSuccess.Should().BeTrue();
        var detection = result.Value.First(d => d.Type == DetectionType.Date);
        detection.Properties.Should().ContainKey("format");
        detection.Properties["format"].Should().BeOneOf("dot", "slash", "dash", "iso", "iso_datetime", "textual", "numeric_time", "ymd_time");
    }

    [Fact]
    public void Detect_IsoDateTime_WithTimezone_ReturnsDetection()
    {
        var document = CreateDocument("Teslim günü 2027-04-14T07:01:07+03:00");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "2027-04-14T07:01:07+03:00");
    }

    [Fact]
    public void Detect_IsoDateTime_WithoutTimezone_ReturnsDetection()
    {
        var document = CreateDocument("2024-09-06T12:02:23");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "2024-09-06T12:02:23");
    }

    [Fact]
    public void Detect_TextualMonth_Full_ReturnsDetection()
    {
        var document = CreateDocument("Randevu 7 Şubat 2028 saat 19:58");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "7 Şubat 2028 saat 19:58");
    }

    [Fact]
    public void Detect_TextualMonth_Abbreviated_ReturnsDetection()
    {
        var document = CreateDocument("01 Şub 2023, 00:31:02");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "01 Şub 2023");
    }

    [Fact]
    public void Detect_TextualMonth_SingleDigitDay_ReturnsDetection()
    {
        var document = CreateDocument("trh:5 Nisan 2026 saat 06:52");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "5 Nisan 2026 saat 06:52");
    }

    [Fact]
    public void Detect_NumericWithTime_ReturnsDetection()
    {
        var document = CreateDocument("Tarih 12.07.2029 17:48");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "12.07.2029 17:48");
    }

    [Fact]
    public void Detect_YmdWithTime_ReturnsDetection()
    {
        var document = CreateDocument("2026/02/02 saat 02.46.16");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "2026/02/02 saat 02.46.16");
    }

    [Fact]
    public void Detect_InvalidDate_31Feb_NotDetected()
    {
        var document = CreateDocument("31.02.2027");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(d => d.Type == DetectionType.Date && d.Value == "31.02.2027");
    }

    [Fact]
    public void Detect_TurkishChars_Preserved()
    {
        var document = CreateDocument("5 Nisan 2026");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value.Contains("Nisan"));
    }

    [Fact]
    public void Detect_FalsePositive_AccountNumber_NotDetected()
    {
        var document = CreateDocument("SPR-2027-VY7ZUB");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.Date).Should().BeEmpty();
    }

    [Fact]
    public void Detect_SingleDigit_DayMonth_8_2_2027_ReturnsDetection()
    {
        var document = CreateDocument("8/2/2027 23.22");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "8/2/2027 23.22");
    }

    [Fact]
    public void Detect_SingleDigit_9_3_2029_ReturnsDetection()
    {
        var document = CreateDocument("9/3/2029 21.32");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "9/3/2029 21.32");
    }

    [Fact]
    public void Detect_SingleDigit_1_7_2025_ReturnsDetection()
    {
        var document = CreateDocument("1/7/2025 15.52");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "1/7/2025 15.52");
    }

    [Fact]
    public void Detect_ZeroPadded_08_02_2027_ReturnsDetection()
    {
        var document = CreateDocument("08/02/2027 23.22");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "08/02/2027 23.22");
    }

    [Fact]
    public void Detect_ZeroPadded_19_07_2024_ReturnsDetection()
    {
        var document = CreateDocument("19/07/2024 00.25");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(d => d.Type == DetectionType.Date && d.Value == "19/07/2024 00.25");
    }

    [Fact]
    public void Detect_Invalid_31_2_2027_NotDetected()
    {
        var document = CreateDocument("31/2/2027 23.22");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.Date && d.Value == "31/2/2027 23.22").Should().BeEmpty();
    }

    [Fact]
    public void Detect_Invalid_32_1_2027_NotDetected()
    {
        var document = CreateDocument("32/1/2027 10.20");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.Date).Should().BeEmpty();
    }

    [Fact]
    public void Detect_Invalid_15_13_2027_NotDetected()
    {
        var document = CreateDocument("15/13/2027 10.20");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.Date).Should().BeEmpty();
    }

    [Fact]
    public void Detect_Invalid_99_99_9999_NotDetected()
    {
        var document = CreateDocument("99/99/9999 10.20");
        var result = _detectionEngine.Detect(document);
        result.IsSuccess.Should().BeTrue();
        result.Value.Where(d => d.Type == DetectionType.Date).Should().BeEmpty();
    }
}