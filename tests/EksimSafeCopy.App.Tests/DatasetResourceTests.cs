using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EksimSafeCopy.App.Services;
using FluentAssertions;
using Xunit;

namespace EksimSafeCopy.App.Tests;

public class DatasetResourceTests
{
    [Fact]
    public void TestJsonl_EmbeddedResource_ExistsInAppAssembly()
    {
        var assembly = typeof(EmbeddedTestDatasetProvider).Assembly;
        var names = assembly.GetManifestResourceNames();
        names.Should().Contain("EksimSafeCopy.App.Resources.test.jsonl");
    }

    [Fact]
    public void TestJsonl_EmbeddedResource_CanBeRead()
    {
        using var stream = EmbeddedTestDatasetProvider.GetDatasetStream();
        stream.Should().NotBeNull();
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TestJsonl_Dataset_NotEmpty()
    {
        var lines = EmbeddedTestDatasetProvider.GetDatasetLines();
        lines.Should().NotBeEmpty();
        lines.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void TestJsonl_LinesAreValidJson()
    {
        var lines = EmbeddedTestDatasetProvider.GetDatasetLines();
        var sample = lines.Take(10).ToArray();
        foreach (var line in sample)
        {
            var doc = JsonDocument.Parse(line);
            doc.RootElement.TryGetProperty("text", out var textProp).Should().BeTrue();
            textProp.GetString().Should().NotBeNullOrEmpty();
            doc.RootElement.TryGetProperty("label", out var labelProp).Should().BeTrue();
            labelProp.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public void TestJsonl_ContainsExpectedCategories()
    {
        var lines = EmbeddedTestDatasetProvider.GetDatasetLines();
        var allLabels = lines.SelectMany(l =>
        {
            var doc = JsonDocument.Parse(l);
            return doc.RootElement.GetProperty("label").EnumerateArray()
                .Select(e => e.GetProperty("category").GetString()!);
        }).Distinct().ToList();

        allLabels.Should().Contain(c => c == "account_number" || c == "vkn" || c == "private_address");
    }

    [Fact]
    public void TestJsonl_GroundTruthFields_Readable()
    {
        var lines = EmbeddedTestDatasetProvider.GetDatasetLines();
        var first = JsonDocument.Parse(lines[0]);
        var text = first.RootElement.GetProperty("text").GetString()!;
        var labels = first.RootElement.GetProperty("label").EnumerateArray().ToList();
        labels.Should().NotBeEmpty();
        var firstLabel = labels[0];
        firstLabel.TryGetProperty("category", out _).Should().BeTrue();
        firstLabel.TryGetProperty("start", out var startProp).Should().BeTrue();
        firstLabel.TryGetProperty("end", out var endProp).Should().BeTrue();
        var start = startProp.GetInt32();
        var end = endProp.GetInt32();
        start.Should().BeGreaterOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        end.Should().BeLessOrEqualTo(text.Length);
        var span = text.Substring(start, end - start);
        span.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TestJsonl_SinglePhysicalCopy_UsedByBothProjects()
    {
        // Verify single physical copy principle via assembly resources, not brittle file paths
        var appAssembly = typeof(EmbeddedTestDatasetProvider).Assembly;
        var testAssembly = typeof(DatasetResourceTests).Assembly;
        // App assembly must have the embedded resource
        appAssembly.GetManifestResourceNames().Should().Contain("EksimSafeCopy.App.Resources.test.jsonl");
        // Test assembly should NOT have its own embedded copy (would indicate second physical source)
        testAssembly.GetManifestResourceNames().Should().NotContain("test.jsonl");
        testAssembly.GetManifestResourceNames().Should().NotContain("EksimSafeCopy.App.Tests.Resources.test.jsonl");
        // Also verify that the test project does not have a loose file copy in output (we read via App assembly)
        var testOutputCopy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "test.jsonl");
        File.Exists(testOutputCopy).Should().BeFalse("Test output should not have a loose file copy - must use App's embedded resource");
        // And verify the single physical source exists on disk (for diagnostic, walk up to repo root)
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EksimSafeCopy.slnx")))
            dir = dir.Parent;
        if (dir != null)
        {
            var singleCopy = Path.Combine(dir.FullName, "src", "EksimSafeCopy.App", "Resources", "test.jsonl");
            File.Exists(singleCopy).Should().BeTrue("Single physical copy must exist at src/EksimSafeCopy.App/Resources/test.jsonl");
        }
    }
}
