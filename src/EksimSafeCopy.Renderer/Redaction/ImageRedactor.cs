namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

public sealed class ImageRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Png;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            using var image = Image.Load<Rgba32>(documentBytes);
            
            var operations = plan.Operations
                .Where(o => o.State == RedactionOperationState.Pending)
                .ToList();

            if (!operations.Any())
            {
                using var ms = new MemoryStream();
                image.Save(ms, GetEncoder(image));
                return Result<byte[]>.Success(ms.ToArray());
            }

            foreach (var op in operations.Where(o => o.State == RedactionOperationState.Pending))
            {
                if (op.BoundingBox.IsEmpty) continue;

                var rect = new Rectangle(
                    (int)Math.Max(0, op.BoundingBox.X),
                    (int)Math.Max(0, op.BoundingBox.Y),
                    (int)Math.Min(image.Width - op.BoundingBox.X, op.BoundingBox.Width),
                    (int)Math.Min(image.Height - op.BoundingBox.Y, op.BoundingBox.Height));

                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var rectF = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
                var fillColor = op.Strategy == RedactionStrategy.FullRedaction 
                    ? new Rgba32(0, 0, 0, 255) 
                    : new Rgba32(128, 128, 128, 255);

                image.Mutate(ctx => ctx.Fill(Color.Black, rectF));
            }

            using var outputStream = new MemoryStream();
            image.Save(outputStream, GetEncoder(image));
            
            return Result<byte[]>.Success(outputStream.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("Image redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"Image redaction failed: {ex.Message}", ex));
        }
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
            return Result<byte[]>.Failure(Error.Cancelled("Image file redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"Image file redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RedactToFile(inputPath, outputPath, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static IImageEncoder GetEncoder(Image<Rgba32> image)
    {
        return new SixLabors.ImageSharp.Formats.Png.PngEncoder();
    }
}