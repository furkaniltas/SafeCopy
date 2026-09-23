namespace SafeCopy.Detectors.Detection.Detectors;

using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class SecretDetector : BaseDetector, ISecretDetector
{
    public override DetectionType Type => DetectionType.Secret;
    public override string Name => "Secret/Credential Detector";
    public override string Description => "Detects passwords, API keys, tokens, private keys and other credentials via label+value semantics";

    // Label+Value patterns - require delimiter : or = and a non-empty value
    // Each pattern captures the credential value in Group 1
    private static readonly Regex[] CredentialPatterns = new[]
    {
        // 1. Password / Şifre / Parola - Turkish + English (include ASCII Sifre)
        new Regex(@"(?:Password|Şifre|Sifre|Parola)\s*[:=]\s*([^\s\n\r""'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 2. API Key variants (include ApiKey without space and ASCII Anahtari)
        new Regex(@"(?:API\s*Key|API_KEY|ApiKey|API\s*Anahtarı|API\s*Anahtari)\s*[:=]\s*([^\s\n\r""'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 3. Secret / Secret Key / Client Secret
        new Regex(@"(?:Client\s+Secret|Secret\s*Key|SECRET_KEY|Secret)\s*[:=]\s*([^\s\n\r""'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 4. Token variants
        new Regex(@"(?:Access\s+Token|ACCESS_TOKEN|Refresh\s+Token|Auth\s+Token|AUTH_TOKEN|Token)\s*[:=]\s*([^\s\n\r""'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 5. Authorization Bearer
        new Regex(@"Authorization\s*:\s*Bearer\s+([A-Za-z0-9\-_\.=]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"Bearer\s+Token\s*[:=]\s*([A-Za-z0-9\-_\.=]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 6. Private Key - PEM block
        new Regex(@"-----BEGIN\s+PRIVATE\s+KEY-----[\s\S]*?-----END\s+PRIVATE\s+KEY-----", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"Private\s*Key\s*[:=]\s*([^\n\r]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 7. Connection String / DB Password
        new Regex(@"(?:Connection\s+String|DB\s+Password|Database\s+Password)\s*[:=]\s*([^\n\r]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 8. Env / config variables
        new Regex(@"\b(?:PASSWORD|PASSWD|PWD|API_KEY|SECRET_KEY|ACCESS_TOKEN|AUTH_TOKEN|CLIENT_SECRET)\s*[:=]\s*([^\s\n\r""'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 8b. Any _SECRET with prefix (e.g., FALCON_CLIENT_SECRET) - capture value, not ID
        new Regex(@"\b[A-Za-z0-9_]*SECRET[A-Za-z0-9_]*\s*=\s*""?([^""\s]+)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 8c. PowerShell / export env syntax: $env:SECRET="value" or export SECRET="value"
        new Regex(@"(?:\$env:)?(?:export\s+)?[A-Za-z0-9_]*SECRET[A-Za-z0-9_]*\s*=\s*""?([^""\s]+)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 8d. Whitespace-separated Secret (e.g., "Secret 28Dlvy..." without : or =)
        new Regex(@"\bSecret\s+([A-Za-z0-9\-_\.]{8,})", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // 9. JSON style "password": "value" - generic credential JSON
        new Regex(@"""(?:password|şifre|sifre|parola|secret|apiKey|api_key|secret_key|client_secret|token|access_token|private_key)""\s*:\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    };

    // False positive headers - if label is followed by these words without value, don't detect
    private static readonly HashSet<string> FalsePositiveLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "policy", "management", "documentation", "validation", "information", "key",
        "primary", "foreign", "keyboard", "management", "policy", "documentation"
    };

    private static readonly HashSet<string> FalsePositivePhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "key management", "primary key", "foreign key", "keyboard", "key information",
        "password policy", "api documentation", "token validation", "secret management"
    };

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        foreach (var pattern in CredentialPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string value;
                int valueStartInMatch;
                int valueLength;

                // PEM block pattern captures whole block as value
                if (pattern.ToString().Contains("BEGIN PRIVATE KEY"))
                {
                    value = match.Value.Trim();
                    valueStartInMatch = 0;
                    valueLength = match.Length;
                }
                else if (match.Groups.Count > 1 && match.Groups[1].Success)
                {
                    value = match.Groups[1].Value.Trim().Trim('"', '\'', '<', '>', '`');
                    // Remove trailing punctuation that is not part of credential (e.g., comma, semicolon)
                    value = value.TrimEnd(',', ';', '.', ')', ']');
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    valueStartInMatch = match.Groups[1].Index - match.Index;
                    valueLength = match.Groups[1].Length;
                    // Adjust for trimming
                    var trimmedStart = match.Groups[1].Value.Length - match.Groups[1].Value.TrimStart().Length;
                    valueStartInMatch += trimmedStart;
                    valueLength = value.Length;
                }
                else
                {
                    value = match.Value.Trim();
                    valueStartInMatch = 0;
                    valueLength = match.Length;
                }

                if (string.IsNullOrWhiteSpace(value)) continue;
                if (value.Length < 3) continue; // too short for credential
                // Skip if value is just a generic header word
                var lowerValue = value.ToLowerInvariant();
                if (FalsePositiveLabelSuffixes.Contains(lowerValue)) continue;
                if (FalsePositivePhrases.Any(p => lowerValue.Contains(p.ToLowerInvariant()))) continue;
                // Skip if value looks like placeholder already
                if (value.StartsWith("[") && value.EndsWith("]")) continue;
                // For Connection String, check if it actually contains password
                if (pattern.ToString().Contains("Connection String") && !value.Contains("Password", StringComparison.OrdinalIgnoreCase) && !value.Contains("Pwd", StringComparison.OrdinalIgnoreCase))
                {
                    // Still consider the whole connection string as secret if it looks like connection string
                    // Keep it
                }

                // Skip false positives like "Key Management" where label is "Key" and value is "Management" (both are generic)
                if (IsFalsePositiveLabelValue(match.Value, value)) continue;

                // Find actual position of value in normalized text
                int valueIndex = match.Index + valueStartInMatch;
                // Verify the value at that position matches (account for trimming)
                // Use the match's value position

                var contextWindow = GetContextWindow(normalizedText, valueIndex, valueLength);
                double confidence = 0.95; // high for label+value

                var originalStart = normalizedText.MapToOriginalPosition(valueIndex);
                var originalEnd = normalizedText.MapToOriginalPosition(valueIndex + valueLength);

                var textSpan = new TextSpan
                {
                    StartIndex = originalStart,
                    Length = originalEnd - originalStart,
                    Text = value,
                    BoundingBox = BoundingBox.Empty
                };

                var detection = CreateDetection(
                    value: value,
                    confidence: confidence,
                    pageNumber: page.PageNumber,
                    textSpan: textSpan,
                    context: contextWindow.FullText,
                    properties: new Dictionary<string, object>
                    {
                        ["credential_type"] = GetCredentialType(match.Value),
                        ["label"] = match.Value.Substring(0, Math.Max(0, match.Value.IndexOf(value, StringComparison.OrdinalIgnoreCase))).Trim().TrimEnd(':', '=', ' ')
                    });

                detections.Add(detection);
            }
        }

        // Deduplicate overlapping detections (keep first)
        var deduped = new List<Detection>();
        foreach (var d in detections.OrderBy(d => d.TextSpan?.StartIndex ?? 0))
        {
            bool overlaps = deduped.Any(existing => existing.TextSpan != null && d.TextSpan != null && SpansOverlap(existing.TextSpan!, d.TextSpan!));
            if (!overlaps)
                deduped.Add(d);
        }

        return deduped;
    }

    private static bool IsFalsePositiveLabelValue(string fullMatch, string value)
    {
        var lower = fullMatch.ToLowerInvariant();
        // Check for known false positive phrases where value is just a generic word
        if (lower.Contains("key management") || lower.Contains("primary key") || lower.Contains("foreign key") ||
            lower.Contains("password policy") || lower.Contains("api documentation") || lower.Contains("token validation") ||
            lower.Contains("secret management") || lower.Contains("keyboard"))
            return true;

        var lowerValue = value.ToLowerInvariant();
        // If value is a single generic word like "Management", "Policy", "Documentation", "Validation", "Information", "Key"
        var genericSingles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "management", "policy", "documentation", "validation", "information", "key", "primary", "foreign", "keyboard" };
        if (genericSingles.Contains(lowerValue)) return true;

        return false;
    }

    private static string GetCredentialType(string label)
    {
        var lower = label.ToLowerInvariant();
        if (lower.Contains("password") || lower.Contains("şifre") || lower.Contains("parola") || lower.Contains("passwd") || lower.Contains("pwd")) return "password";
        if (lower.Contains("api") && lower.Contains("key")) return "api_key";
        if (lower.Contains("secret")) return "secret";
        if (lower.Contains("private") && lower.Contains("key")) return "private_key";
        if (lower.Contains("token") || lower.Contains("bearer") || lower.Contains("authorization")) return "token";
        if (lower.Contains("connection") || lower.Contains("database")) return "connection_string";
        return "credential";
    }
}
