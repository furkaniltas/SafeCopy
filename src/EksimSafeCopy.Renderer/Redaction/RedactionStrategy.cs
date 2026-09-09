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

    public string GetReplacementText(DetectionType type, string originalValue, RenderOptions options)
    {
        if (string.IsNullOrEmpty(originalValue)) return GetReplacementText(type, options);
        return PartialMaskingPolicy.Mask(type, originalValue);
    }

    public bool SupportsFormat(DocumentFormat format)
    {
        return format != DocumentFormat.Unknown;
    }
}

internal static class PartialMaskingPolicy
{
    public static string Mask(DetectionType type, string value)
    {
        if (string.IsNullOrEmpty(value)) return "[REDACTED]";
        return type switch
        {
            DetectionType.TcKimlikNo => MaskTcKimlikNo(value),
            DetectionType.Phone => MaskPhone(value),
            DetectionType.Iban => MaskIban(value),
            DetectionType.TesisatNo or DetectionType.AboneNo or DetectionType.SayacNo or DetectionType.MusteriNo or DetectionType.DosyaNo or DetectionType.DavaNo => MaskInstallationNumber(value),
            DetectionType.FullName or DetectionType.FirstName or DetectionType.LastName or DetectionType.MotherName or DetectionType.FatherName => MaskFullName(value),
            DetectionType.Date or DetectionType.CardExpiry => MaskDate(value),
            DetectionType.TaxId or DetectionType.SgkNo => MaskTaxId(value),
            DetectionType.CreditCard => MaskCreditCard(value),
            DetectionType.Email => MaskEmail(value),
            DetectionType.Address => MaskAddress(value),
            DetectionType.Secret => MaskSecret(value),
            DetectionType.Username or DetectionType.Cvv or DetectionType.BloodType or DetectionType.IpAddress or DetectionType.MacAddress or DetectionType.LicensePlate => MaskGeneric(value),
            _ => MaskGeneric(value)
        };
    }

    private static string MaskTcKimlikNo(string v)
    {
        var digits = new string(v.Where(char.IsDigit).ToArray());
        if (digits.Length != 11) return new string('*', v.Length);
        return new string('*', 7) + digits.Substring(7, 4);
    }

    private static string MaskPhone(string v)
    {
        // Preserve formatting characters, mask middle digits, keep first 4 and last 2 digits visible
        var digits = new string(v.Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return new string('*', v.Length);
        // For 10-11 digit phones, keep first 4 and last 2
        var result = new System.Text.StringBuilder(v.Length);
        int digitIndex = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (!char.IsDigit(v[i])) { result.Append(v[i]); continue; }
            // Keep first 4 and last 2 digits
            if (digitIndex < 4 || digitIndex >= digits.Length - 2) result.Append(v[i]);
            else result.Append('*');
            digitIndex++;
        }
        return result.ToString();
    }

    private static string MaskIban(string v)
    {
        // Keep TR + first 2 and last 2, mask middle, preserve spaces if any
        var stripped = v.Replace(" ", "").Replace("-", "");
        if (stripped.Length < 8) return new string('*', v.Length);
        var result = new System.Text.StringBuilder(v.Length);
        int strippedIndex = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == ' ' || v[i] == '-') { result.Append(v[i]); continue; }
            // Map v[i] to stripped position
            // Find corresponding stripped index by counting non-space chars up to i
            int countBefore = v.Take(i).Count(c => c != ' ' && c != '-');
            strippedIndex = countBefore;
            if (strippedIndex < 4 || strippedIndex >= stripped.Length - 2) result.Append(v[i]);
            else result.Append('*');
        }
        return result.ToString();
    }

    private static string MaskInstallationNumber(string v)
    {
        var alnum = new string(v.Where(char.IsLetterOrDigit).ToArray());
        if (alnum.Length < 6) return new string('*', v.Length);
        // Keep last 4
        var keep = Math.Min(4, alnum.Length);
        var maskLen = alnum.Length - keep;
        var maskedAlnum = new string('*', maskLen) + alnum.Substring(maskLen, keep);
        // Map back to original with formatting
        var result = new System.Text.StringBuilder(v.Length);
        int ai = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (!char.IsLetterOrDigit(v[i])) { result.Append(v[i]); continue; }
            result.Append(maskedAlnum[ai++]);
        }
        return result.ToString();
    }

    private static string MaskFullName(string v)
    {
        var parts = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return new string('*', v.Length);
        var maskedParts = parts.Select(p => p.Length <= 1 ? p : p[0].ToString() + new string('*', p.Length - 1));
        // Reconstruct with original spacing (single space between words, but preserve original spaces)
        // For simplicity, join with single space
        return string.Join(" ", maskedParts);
    }

    private static string MaskDate(string v)
    {
        // Keep year (last 4 digits after separator), mask day/month: 18/08/1969 -> **/**/1969
        var digits = new string(v.Where(char.IsDigit).ToArray());
        if (digits.Length < 8) return new string('*', v.Length);
        var year = digits.Substring(digits.Length - 4, 4);
        var result = new System.Text.StringBuilder(v.Length);
        int di = 0;
        // Find year position in original
        int yearStartInDigits = digits.Length - 4;
        for (int i = 0; i < v.Length; i++)
        {
            if (!char.IsDigit(v[i])) { result.Append(v[i]); continue; }
            if (di >= yearStartInDigits) result.Append(v[i]);
            else result.Append('*');
            di++;
        }
        return result.ToString();
    }

    private static string MaskTaxId(string v)
    {
        var digits = new string(v.Where(char.IsDigit).ToArray());
        if (digits.Length < 8) return new string('*', v.Length);
        return new string('*', digits.Length - 4) + digits.Substring(digits.Length - 4, 4);
    }

    private static string MaskCreditCard(string v)
    {
        var digits = new string(v.Where(char.IsDigit).ToArray());
        if (digits.Length < 13) return new string('*', v.Length);
        // Keep last 4
        var masked = new string('*', digits.Length - 4) + digits.Substring(digits.Length - 4, 4);
        // Preserve formatting
        var result = new System.Text.StringBuilder(v.Length);
        int di = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (!char.IsDigit(v[i])) { result.Append(v[i]); continue; }
            result.Append(masked[di++]);
        }
        return result.ToString();
    }

    private static string MaskEmail(string v)
    {
        var atIdx = v.IndexOf('@');
        if (atIdx <= 1) return new string('*', v.Length);
        var local = v.Substring(0, atIdx);
        var domain = v.Substring(atIdx);
        var maskedLocal = local[0].ToString() + new string('*', Math.Max(1, local.Length - 1));
        return maskedLocal + domain;
    }

    private static string MaskAddress(string v)
    {
        // For address, mask with placeholder as it's free text and hard to partially mask securely
        return "[ADDRESS]";
    }

    private static string MaskSecret(string v)
    {
        // Password/Secret must be fully hidden even in PartialMask, never show any original characters
        return "[SECRET]";
    }

    private static string MaskGeneric(string v)
    {
        if (v.Length <= 4) return new string('*', v.Length);
        return new string('*', v.Length - 4) + v.Substring(v.Length - 4, 4);
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