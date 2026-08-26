using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Docx;
using EksimSafeCopy.DocumentEngine.Ingestion.Image;
using EksimSafeCopy.DocumentEngine.Ingestion.Pdf;
using EksimSafeCopy.DocumentEngine.Ingestion.Txt;
using EksimSafeCopy.DocumentEngine.Ingestion.Udf;
using EksimSafeCopy.DocumentEngine.Ingestion.Xlsx;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EksimSafeCopy.App.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        return services;
    }

    public static IServiceCollection AddDocumentEngine(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentIngestor, PdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, DocxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, TxtDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, UdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, ImageDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocumentEngine.Ingestion.DocumentEngine>();
        return services;
    }
}
