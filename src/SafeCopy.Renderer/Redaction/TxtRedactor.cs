namespace SafeCopy.Renderer.Redaction;

using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

public sealed class TxtRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Txt;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var encoding = DetectEncoding(documentBytes);
            var originalText = encoding.GetString(documentBytes);

            // Remove BOM if present
            if (originalText.Length > 0 && originalText[0] == '\uFEFF')
            {
                originalText = originalText.Substring(1);
            }

            var operations = plan.Operations
                .Where(o => o.State == RedactionOperationState.Pending)
                .OrderByDescending(o => o.TextSpan?.StartIndex ?? 0)
                .ToList();

            var textBuilder = new StringBuilder(originalText);

            foreach (var op in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (op.TextSpan == null) continue;

                var start = op.TextSpan.StartIndex;
                var length = op.TextSpan.Length;

                if (start >= 0 && start + length <= textBuilder.Length)
                {
                    // Verify the text matches
                    var actualText = textBuilder.ToString(start, length);
                    if (actualText != op.TextSpan.Text)
                    {
                        // Try to find the text near the expected position
                        var foundIndex = FindTextNearPosition(textBuilder.ToString(), op.TextSpan.Text, start);
                        if (foundIndex >= 0)
                        {
                            start = foundIndex;
                            length = op.TextSpan.Text.Length;
                        }
                        else
                        {
                            continue; // Text not found, skip
                        }
                    }

                    var replacement = op.Strategy == RedactionStrategy.FullRedaction
                        ? new string('█', length)
                        : op.ReplacementText ?? op.DetectionType.ToString();

                    textBuilder.Remove(start, length);
                    textBuilder.Insert(start, replacement);
                }
            }

            var resultText = textBuilder.ToString();
            var resultBytes = encoding.GetBytes(resultText);

            return Result<byte[]>.Success(resultBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("TXT redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"TXT redaction failed: {ex.Message}", ex));
        }
    }

    private static int FindTextNearPosition(string text, string searchText, int expectedPosition)
    {
        var startSearch = Math.Max(0, expectedPosition - 50);
        var endSearch = Math.Min(text.Length - searchText.Length, expectedPosition + 50);

        for (int i = Math.Max(0, expectedPosition - 50); i <= Math.Min(text.Length - searchText.Length, expectedPosition + 50); i++)
        {
            if (i + searchText.Length <= text.Length && text.Substring(i, searchText.Length) == searchText)
            {
                return i;
            }
        }
        return -1;
    }

    public async Task<Result<byte[]>> RedactAsync(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Redact(documentBytes, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Result<byte[]> RedactToFile(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = File.ReadAllBytes(inputPath);
            var result = Redact(bytes, plan, options, cancellationToken);

            if (result.IsFailure)
                return Result<byte[]>.Failure(result.Error);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir!);

            var tempPath = outputPath + ".tmp";
            File.WriteAllBytes(tempPath, result.Value);
            File.Move(tempPath, outputPath, true);

            return Result<byte[]>.Success(result.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("TXT file redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"TXT file redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RedactToFile(inputPath, outputPath, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
            if (bytes[0] == 0xFE && bytes[1] == 0xFE)
                return Encoding.BigEndianUnicode;
        }

        if (IsValidUtf8(bytes))
            return new UTF8Encoding(false);

        return Encoding.GetEncoding("windows-1254");
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] <= 0x7F)
            {
                i++;
            }
            else if ((bytes[i] & 0xE0) == 0xC0)
            {
                if (i + 1 >= bytes.Length || (bytes[i + 1] & 0xC0) != 0x80) return false;
                i += 2;
            }
            else if ((bytes[i] & 0xF0) == 0xE0)
            {
                if (i + 2 >= bytes.Length || (bytes[i + 1] & 0xC0) != 0x80 || (bytes[i + 2] & 0xC0) != 0x80) return false;
                i += 3;
            }
            else if ((bytes[i] & 0xF8) == 0xF0)
            {
                if (i + 3 >= bytes.Length || (bytes[i + 1] & 0xC0) != 0x80 || (bytes[i + 2] & 0xC0) != 0x80 || (bytes[i + 3] & 0xC0) != 0x80) return false;
                i += 4;
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}