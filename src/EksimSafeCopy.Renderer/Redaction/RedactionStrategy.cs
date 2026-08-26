namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;

public sealed class DefaultRedactionStrategy : IRedactionStrategy
{
    public RedactionStrategy Type => RedactionStrategy.TypeLabel;

    public string GetReplacementText(DetectionType type, RenderOptions options)
    {
        if (options.UseTypePlaceholder && options.TypePlaceholders.TryGetValue(type, out var placeholder))
        {
            return placeholder;
        }

        return options.PlaceholderText;
    }

    public bool SupportsFormat(DocumentFormat format)
    {
        return format != DocumentFormat.Unknown;
    }
}

public sealed class FullRedactionStrategy : IRedactionStrategy
{
    public RedactionStrategy Type => RedactionStrategy.FullRedaction;

    public string GetReplacementText(DetectionType type, RenderOptions options)
    {
        return string.Empty;
    }

    public bool SupportsFormat(DocumentFormat format)
    {
        return format != DocumentFormat.Unknown;
    }
}

public sealed class PlaceholderStrategy : IRedactionStrategy
{
    public RedactionStrategy Type => RedactionStrategy.Placeholder;

    public string GetReplacementText(DetectionType type, RenderOptions options)
    {
        return options.PlaceholderText;
    }

    public bool SupportsFormat(DocumentFormat format)
    {
        return format != DocumentFormat.Unknown;
    }
}

public sealed class PartialMaskStrategy : IRedactionStrategy
{
    public RedactionStrategy Type => RedactionStrategy.PartialMask;

    public string GetReplacementText(DetectionType type, RenderOptions options)
    {
        return options.PlaceholderText;
    }

    public bool SupportsFormat(DocumentFormat format)
    {
        return format != DocumentFormat.Unknown;
    }
}

public static class RedactionStrategyFactory
{
    public static IRedactionStrategy Create(RedactionStrategy strategy)
    {
        return strategy switch
        {
            RedactionStrategy.FullRedaction => new FullRedactionStrategy(),
            RedactionStrategy.TypeLabel => new DefaultRedactionStrategy(),
            RedactionStrategy.Placeholder => new PlaceholderStrategy(),
            RedactionStrategy.PartialMask => new PartialMaskStrategy(),
            RedactionStrategy.Custom => new DefaultRedactionStrategy(),
            _ => new DefaultRedactionStrategy()
        };
    }
}