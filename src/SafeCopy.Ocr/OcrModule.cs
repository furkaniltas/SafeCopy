namespace SafeCopy.Ocr;

using SafeCopy.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for OCR engine. Local-only, no cloud.
/// </summary>
public static class OcrModule
{
    public static IServiceCollection AddOcr(this IServiceCollection services)
    {
        services.AddSingleton<SecureImagePreprocessor>();
        services.AddSingleton<IOcrEngine, LocalOcrEngine>();
        return services;
    }
}
