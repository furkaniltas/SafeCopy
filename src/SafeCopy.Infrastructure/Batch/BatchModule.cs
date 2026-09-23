namespace SafeCopy.Infrastructure.Batch;

using SafeCopy.Core.Abstractions;
using SafeCopy.DocumentEngine.Security;
using Microsoft.Extensions.DependencyInjection;

public static class BatchModule
{
    public static IServiceCollection AddBatch(this IServiceCollection services)
    {
        services.AddSingleton<IBatchProcessor>(sp => new BatchProcessor(
            sp.GetRequiredService<IDocumentEngine>(),
            sp.GetRequiredService<IDetectionEngine>(),
            sp.GetRequiredService<IRenderer>(),
            sp.GetRequiredService<IRedactionPlanner>(),
            sp.GetRequiredService<IVerificationEngine>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IDocumentSecurityValidator>()));
        return services;
    }
}
