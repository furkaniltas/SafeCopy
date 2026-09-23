using System.IO;
using System.Reflection;

namespace SafeCopy.App.Services;

/// <summary>
/// Provides access to the embedded test.jsonl dataset for reference/testing purposes.
/// The dataset is embedded as a resource in the main App assembly and shared with test projects
/// via assembly resource access, ensuring a single physical copy.
/// </summary>
public static class EmbeddedTestDatasetProvider
{
    private const string ResourceName = "SafeCopy.App.Resources.test.jsonl";

    public static Stream GetDatasetStream()
    {
        var assembly = typeof(EmbeddedTestDatasetProvider).Assembly;
        var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        return stream;
    }

    public static string[] GetDatasetLines()
    {
        using var stream = GetDatasetStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }
        return lines.ToArray();
    }

    public static bool IsDatasetAvailable()
    {
        var assembly = typeof(EmbeddedTestDatasetProvider).Assembly;
        return assembly.GetManifestResourceNames().Contains(ResourceName);
    }
}
