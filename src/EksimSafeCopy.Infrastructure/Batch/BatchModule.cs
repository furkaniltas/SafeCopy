namespace EksimSafeCopy.Infrastructure.Batch;

using EksimSafeCopy.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class BatchModule
{
    public static IServiceCollection AddBatch(this IServiceCollection services)
    {
        services.AddSingleton<IBatchProcessor, BatchProcessor>();
        return services;
    }
}
