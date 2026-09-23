using System.Linq;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using SafeCopy.Detectors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;

namespace SafeCopy.Detectors.Tests.Detectors;

public class IbanDetectorTests
{
    private readonly IDetectionEngine _engine;

    public IbanDetectorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();
        var sp = services.BuildServiceProvider();
        _engine = sp.GetRequiredService<IDetectionEngine>();
    }

    private Document Doc(string text)
    {
        var page = new DocumentPage
        {
            PageNumber = 0, Width = 800, Height = 600, DpiX = 96, DpiY = 96,
            Text = text,
            TextBlocks = new[] { new TextBlock { Text = text, Type = TextBlockType.Paragraph, Direction = TextDirection.LeftToRight, OrderIndex = 0, PageNumber = 0 } }.ToList().AsReadOnly()
        };
        return new Document { Name = "test.txt", Format = DF.Txt, Pages = new[] { page }, Metadata = new DocumentMetadata() };
    }

    [Fact]
    public void ValidIban_Spaced_Detected()
    {
        var all = _engine.Detect(Doc("TR33 0006 1005 1978 6457 8413 26")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.Iban && d.Value == "TR33 0006 1005 1978 6457 8413 26");
        all.Single(d => d.Type == DetectionType.Iban).Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void ValidIban_Unspaced_Detected()
    {
        var all = _engine.Detect(Doc("TR330006100519786457841326")).Value;
        all.Should().ContainSingle(d => d.Type == DetectionType.Iban && d.Value == "TR330006100519786457841326");
    }

    [Fact]
    public void InvalidChecksum_NotDetected()
    {
        var all = _engine.Detect(Doc("TR00 0000 0000 0000 0000 0000 00")).Value;
        all.Where(d => d.Type == DetectionType.Iban).Should().BeEmpty();
    }

    [Fact]
    public void WrongLength_NotDetected()
    {
        var all = _engine.Detect(Doc("TR33 0006 1005 1978 6457 8413 2")).Value;
        all.Where(d => d.Type == DetectionType.Iban).Should().BeEmpty();
        var all2 = _engine.Detect(Doc("TR33")).Value;
        all2.Where(d => d.Type == DetectionType.Iban).Should().BeEmpty();
    }

    [Fact]
    public void Lowercase_Normalized_Detected()
    {
        var all = _engine.Detect(Doc("tr33 0006 1005 1978 6457 8413 26")).Value;
        all.Where(d => d.Type == DetectionType.Iban).Should().HaveCount(1);
        all.Single(d => d.Type == DetectionType.Iban).Value.Should().Be("tr33 0006 1005 1978 6457 8413 26");
        all.Single(d => d.Type == DetectionType.Iban).Properties["iban_normalized"].Should().Be("TR330006100519786457841326");
    }

    [Fact]
    public void Span_OnlyIban_NotLabel()
    {
        var all = _engine.Detect(Doc("IBAN: TR33 0006 1005 1978 6457 8413 26")).Value;
        var iban = all.Single(d => d.Type == DetectionType.Iban);
        iban.TextSpan!.Text.Should().Be("TR33 0006 1005 1978 6457 8413 26");
        iban.TextSpan!.Text.Should().NotContain("IBAN:");
        iban.Value.Should().Be("TR33 0006 1005 1978 6457 8413 26");
    }

    [Fact]
    public void InvalidIban_NoDetection()
    {
        var all = _engine.Detect(Doc("TR00 0000 0000 0000 0000 0000 00")).Value;
        all.Where(d => d.Type == DetectionType.Iban).Should().BeEmpty();
    }
}
