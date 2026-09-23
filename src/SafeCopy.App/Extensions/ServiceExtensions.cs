using SafeCopy.Core.Abstractions;
using SafeCopy.DocumentEngine.Ingestion;
using SafeCopy.DocumentEngine.Ingestion.Docx;
using SafeCopy.DocumentEngine.Ingestion.Image;
using SafeCopy.DocumentEngine.Ingestion.Pdf;
using SafeCopy.DocumentEngine.Ingestion.Txt;
using SafeCopy.DocumentEngine.Ingestion.Udf;
using SafeCopy.DocumentEngine.Ingestion.Xlsx;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace SafeCopy.App.Extensions;

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
