using System.Text;
using EksimSafeCopy.Benchmark.Matching;
using EksimSafeCopy.Benchmark.Metrics;
using EksimSafeCopy.Benchmark.Models;
using EksimSafeCopy.Benchmark.Parser;
using FluentAssertions;
using Xunit;

namespace EksimSafeCopy.Benchmark.Tests;

public class BenchmarkTests
{
    [Fact]
    public void JsonlParsing_SingleRecord()
    {
        var line = "{\"text\":\"Ayşe Demir 123\",\"label\":[{\"category\":\"full_name\",\"start\":0,\"end\":10}]}";
        var rec = BenchmarkParser.ParseLine(line, 1);
        rec.IsMalformed.Should().BeFalse();
        rec.Text.Should().Be("Ayşe Demir 123");
        rec.Entities.Should().HaveCount(1);
        rec.Entities[0].Label.Should().Be("full_name");
        rec.Entities[0].Start.Should().Be(0);
        rec.Entities[0].End.Should().Be(10);
    }

    [Fact]
    public void EmptySpans_Handled()
    {
        var line = "{\"text\":\"Selam dünya\",\"label\":[]}";
        var rec = BenchmarkParser.ParseLine(line, 1);
        rec.IsMalformed.Should().BeFalse();
        rec.IsEmptySpans.Should().BeTrue();
        rec.Entities.Should().BeEmpty();
    }

    [Fact]
    public void MultipleEntities_Parsed()
    {
        var line = "{\"text\":\"Ahmet Yılmaz 0532 123 45 67\",\"label\":[{\"category\":\"full_name\",\"start\":0,\"end\":12},{\"category\":\"phone\",\"start\":13,\"end\":27}]}";
        var rec = BenchmarkParser.ParseLine(line, 1);
        rec.Entities.Should().HaveCount(2);
        rec.Entities[0].Label.Should().Be("full_name");
        rec.Entities[1].Label.Should().Be("phone");
    }

    [Fact]
    public void TurkishUtf8_Preserved()
    {
        var text = "Çankaya Mahallesi Atatürk Caddesi ĞüşİÖÇ";
        var line = $"{{\"text\":\"{text}\",\"label\":[{{\"category\":\"private_address\",\"start\":0,\"end\":17}}]}}";
        var rec = BenchmarkParser.ParseLine(line, 1);
        rec.Text.Should().Be(text);
        rec.Entities[0].Text.Should().Be("Çankaya Mahallesi");
    }

    [Fact]
    public void LabelMapping_DirectAndMapped()
    {
        LabelMappingTable.Resolve("tckn").Status.Should().Be(MappingStatus.DIRECT);
        LabelMappingTable.Resolve("vkn").Status.Should().Be(MappingStatus.MAPPED);
        LabelMappingTable.Resolve("vkn").SafeCopyType.Should().Be(EksimSafeCopy.Core.Models.DetectionType.TaxId);
    }

    [Fact]
    public void UnsupportedLabels_NotCountedAsFailure()
    {
        var m = LabelMappingTable.Resolve("private_url");
        m.Status.Should().Be(MappingStatus.UNSUPPORTED);
        m.SafeCopyType.Should().BeNull();
        var m2 = LabelMappingTable.Resolve("account_number");
        m2.Status.Should().Be(MappingStatus.AMBIGUOUS);
    }

    [Fact]
    public void ExactSpanMatching()
    {
        var rec = new BenchmarkRecord { Id = "1", Text = "Ahmet Yılmaz", Entities = new List<GroundTruthEntity> { new() { Label = "full_name", Start = 0, End = 12, Text = "Ahmet Yılmaz", MappedType = EksimSafeCopy.Core.Models.DetectionType.FullName, MappingStatus = MappingStatus.DIRECT } } };
        var det = new BenchmarkDetection { Type = EksimSafeCopy.Core.Models.DetectionType.FullName, Value = "Ahmet Yılmaz", Start = 0, End = 12, Text = "Ahmet Yılmaz" };
        var matches = SpanMatcher.MatchRecord(rec, new List<BenchmarkDetection> { det });
        matches.Should().ContainSingle(m => m.Kind == MatchKind.Exact);
    }

    [Fact]
    public void PartialOverlap_BoundaryMismatch()
    {
        var rec = new BenchmarkRecord { Id = "1", Text = "Ahmet Yılmaz", Entities = new List<GroundTruthEntity> { new() { Label = "full_name", Start = 0, End = 12, Text = "Ahmet Yılmaz", MappedType = EksimSafeCopy.Core.Models.DetectionType.FullName, MappingStatus = MappingStatus.DIRECT } } };
        var det = new BenchmarkDetection { Type = EksimSafeCopy.Core.Models.DetectionType.FullName, Value = "Ahmet", Start = 0, End = 5, Text = "Ahmet" };
        var matches = SpanMatcher.MatchRecord(rec, new List<BenchmarkDetection> { det });
        matches.Should().ContainSingle(m => m.Kind == MatchKind.BoundaryMismatch);
    }

    [Fact]
    public void OneToOneMatching_Deterministic()
    {
        var rec = new BenchmarkRecord
        {
            Id = "1",
            Text = "Ahmet Yılmaz Mehmet Kaya",
            Entities = new List<GroundTruthEntity>
            {
                new() { Label = "full_name", Start = 0, End = 12, Text = "Ahmet Yılmaz", MappedType = EksimSafeCopy.Core.Models.DetectionType.FullName, MappingStatus = MappingStatus.DIRECT },
                new() { Label = "full_name", Start = 13, End = 24, Text = "Mehmet Kaya", MappedType = EksimSafeCopy.Core.Models.DetectionType.FullName, MappingStatus = MappingStatus.DIRECT }
            }
        };
        var dets = new List<BenchmarkDetection>
        {
            new() { Type = EksimSafeCopy.Core.Models.DetectionType.FullName, Value = "Ahmet Yılmaz", Start = 0, End = 12, Text = "Ahmet Yılmaz" },
            new() { Type = EksimSafeCopy.Core.Models.DetectionType.FullName, Value = "Ahmet Yılmaz", Start = 0, End = 12, Text = "Ahmet Yılmaz" } // duplicate
        };
        var matches = SpanMatcher.MatchRecord(rec, dets);
        // One detection should match one GT, the duplicate should be FP, not match second GT
        matches.Count(m => m.Kind == MatchKind.Exact).Should().Be(1);
        matches.Count(m => m.Kind == MatchKind.FalsePositive).Should().Be(1);
        matches.Count(m => m.Kind == MatchKind.Missed).Should().Be(1);
    }

    [Fact]
    public void FpCalculation()
    {
        var rec = new BenchmarkRecord { Id = "1", Text = "Selam", Entities = new List<GroundTruthEntity>() };
        var det = new BenchmarkDetection { Type = EksimSafeCopy.Core.Models.DetectionType.Phone, Value = "0532", Start = 0, End = 4, Text = "0532" };
        var matches = SpanMatcher.MatchRecord(rec, new List<BenchmarkDetection> { det });
        matches.Should().ContainSingle(m => m.Kind == MatchKind.FalsePositive);
        var metrics = MetricsCalculator.ComputeOverall(matches);
        metrics.FP.Should().Be(1);
        metrics.TP.Should().Be(0);
    }

    [Fact]
    public void FnCalculation()
    {
        var rec = new BenchmarkRecord { Id = "1", Text = "Ahmet Yılmaz", Entities = new List<GroundTruthEntity> { new() { Label = "full_name", Start = 0, End = 12, Text = "Ahmet Yılmaz", MappedType = EksimSafeCopy.Core.Models.DetectionType.FullName, MappingStatus = MappingStatus.DIRECT } } };
        var matches = SpanMatcher.MatchRecord(rec, new List<BenchmarkDetection>());
        matches.Should().ContainSingle(m => m.Kind == MatchKind.Missed);
        var metrics = MetricsCalculator.ComputeOverall(matches);
        metrics.FN.Should().Be(1);
    }

    [Fact]
    public void PrecisionRecallF1_Calculation()
    {
        var allMatches = new List<Benchmark.Models.EntityMatch>
        {
            new() { Kind = MatchKind.Exact, GroundTruth = new GroundTruthEntity(), Detection = new BenchmarkDetection() },
            new() { Kind = MatchKind.Exact, GroundTruth = new GroundTruthEntity(), Detection = new BenchmarkDetection() },
            new() { Kind = MatchKind.Missed, GroundTruth = new GroundTruthEntity() },
            new() { Kind = MatchKind.FalsePositive, Detection = new BenchmarkDetection() }
        };
        var m = MetricsCalculator.ComputeOverall(allMatches);
        m.TP.Should().Be(2);
        m.FN.Should().Be(1);
        m.FP.Should().Be(1);
        m.Precision.Should().BeApproximately(0.666, 0.01);
        m.Recall.Should().BeApproximately(0.666, 0.01);
        m.F1.Should().BeApproximately(0.666, 0.01);
    }

    [Fact]
    public void DeterministicResults()
    {
        var rec = new BenchmarkRecord { Id = "1", Text = "Ahmet Yılmaz", Entities = new List<GroundTruthEntity> { new() { Label = "full_name", Start = 0, End = 12, Text = "Ahmet Yılmaz", MappedType = EksimSafeCopy.Core.Models.DetectionType.FullName, MappingStatus = MappingStatus.DIRECT } } };
        var det = new BenchmarkDetection { Type = EksimSafeCopy.Core.Models.DetectionType.FullName, Value = "Ahmet Yılmaz", Start = 0, End = 12, Text = "Ahmet Yılmaz" };
        var m1 = SpanMatcher.MatchRecord(rec, new List<BenchmarkDetection> { det });
        var m2 = SpanMatcher.MatchRecord(rec, new List<BenchmarkDetection> { det });
        m1[0].Kind.Should().Be(m2[0].Kind);
        m1[0].IoU.Should().Be(m2[0].IoU);
    }

    [Fact]
    public void MalformedRecord_Reported()
    {
        var rec = BenchmarkParser.ParseLine("not json", 5);
        rec.IsMalformed.Should().BeTrue();
        rec.ParseError.Should().NotBeNullOrEmpty();
        rec.LineNumber.Should().Be(5);
    }
}
