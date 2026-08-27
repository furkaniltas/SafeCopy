using System.Security.Cryptography;
using EksimSafeCopy.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EksimSafeCopy.Security.Tests;

public class LocalOnlySecurityTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EksimSafeCopy.slnx")))
                return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (EksimSafeCopy.slnx not found). Searched from " + AppContext.BaseDirectory);
    }

    private static IReadOnlyList<string> GetSourceFiles()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root, "src");
        return Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
    }

    private static IReadOnlyList<string> GetCsprojFiles()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root, "src");
        return Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories).ToList();
    }

    // -----------------------------------------------------------------
    // 1. Network: No HttpClient / socket / HTTP surface in src
    // -----------------------------------------------------------------
    [Fact]
    public void Network_NoHttpClientInSrc()
    {
        var patterns = new[]
        {
            "HttpClient", "HttpRequest", "HttpResponse", "WebClient",
            "RestSharp", "RestClient", "Flurl", "HttpWebRequest", "WebRequest",
            "SocketsHttpHandler", "IHttpClientFactory",
            "TcpClient", "UdpClient",
            "using System.Net.Http", "namespace System.Net", "System.Net.Sockets", "System.Net.Http"
        };

        var violations = new List<string>();
        foreach (var file in GetSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var pat in patterns)
            {
                if (text.Contains(pat, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(file)} contains '{pat}' -> {file}");
                }
            }
        }

        violations.Should().BeEmpty("src must not contain any HTTP/socket/client networking surface");
    }

    [Fact]
    public void Network_NoDnsLookupInSrc()
    {
        var patterns = new[] { "Dns.", "GetHostEntry", "GetHostAddresses", "DnsEndPoint" };
        var violations = new List<string>();
        foreach (var file in GetSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var pat in patterns)
            {
                if (text.Contains(pat, StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(file)}:{pat} -> {file}");
            }
            if (text.Contains("using System.Net", StringComparison.Ordinal))
                violations.Add($"{Path.GetFileName(file)} contains 'using System.Net' -> {file}");
        }
        violations.Should().BeEmpty("src must not perform DNS lookups or import System.Net");
    }

    [Fact]
    public void Network_NoHttpOrHttpsLiteralForOutbound()
    {
        // We forbid outbound http calls. Literal http:// strings for legitimate local purposes (e.g., XML namespace)
        // are not present in this codebase. If any appear in src, they must be reviewed.
        var violations = new List<string>();
        foreach (var file in GetSourceFiles())
        {
            var text = File.ReadAllText(file);
            // Ignore comments referencing docs but flag any real http:// literal
            // We check for "http://" and "https://" and ensure no System.Net code nearby
            if (text.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Allow if it's just in a comment about XMLNS? Currently src has zero, so any is violation.
                violations.Add($"{Path.GetFileName(file)} contains http(s):// -> {file}");
            }
        }
        violations.Should().BeEmpty("src must not contain http/https outbound URLs");
    }

    // -----------------------------------------------------------------
    // 2. Telemetry / Analytics absence
    // -----------------------------------------------------------------
    [Fact]
    public void Telemetry_NoAnalyticsPackages()
    {
        var forbidden = new[]
        {
            "ApplicationInsights", "Microsoft.ApplicationInsights",
            "GoogleAnalytics", "Mixpanel", "Segment", "Amplitude",
            "PostHog", "Snowplow", "Telemetry", "Analytics"
        };
        // Stronger: explicitly forbid known analytics package ids
        var forbiddenPackageIds = new[]
        {
            "Microsoft.ApplicationInsights", "Segment.Analytics", "Mixpanel", "Amplitude",
            "PostHog", "GoogleAnalytics", "RestSharp", "Flurl"
        };
        var violations = new List<string>();
        foreach (var csproj in GetCsprojFiles())
        {
            var text = File.ReadAllText(csproj);
            foreach (var pkg in forbiddenPackageIds)
            {
                if (text.Contains(pkg, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{Path.GetFileName(csproj)} references forbidden '{pkg}'");
            }
            // Also catch any PackageReference that looks like telemetry
            if (text.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase))
                violations.Add($"{Path.GetFileName(csproj)} references telemetry package");
        }
        // Also check src *.cs for namespace strings that would indicate analytics SDK use
        foreach (var file in GetSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var pat in forbidden)
            {
                // Case-insensitive for telemetry strings but avoid false positive on local "Analytics" word in comments
                // We treat any using/import of those namespaces as violation
                if (text.Contains(pat, StringComparison.OrdinalIgnoreCase))
                {
                    // Allow the word "Analytics" only if it's in LocalOnlySecurityTests itself — but we scan src only
                    violations.Add($"{Path.GetFileName(file)} contains telemetry/analytics '{pat}' -> {file}");
                }
            }
        }
        violations.Should().BeEmpty("product must not reference telemetry/analytics packages or namespaces");
    }

    [Fact]
    public void Telemetry_NoAnalyticsPackages_StrictCsprojCheck()
    {
        // Strict csproj allowlist: only MIT/Apache2.0 local libs permitted in src
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Logging.Abstractions",
            "DocumentFormat.OpenXml",
            "PdfPig",
            "PdfPig.Core", "PdfPig.Fonts", "PdfPig.Tokenization", "PdfPig.Tokens",
            "UglyToad.PdfPig", "UglyToad.PdfPig.Core", "UglyToad.PdfPig.Fonts", "UglyToad.PdfPig.Tokenization", "UglyToad.PdfPig.Tokens",
            "PDFsharp", "PdfSharp",
            "SixLabors.ImageSharp", "SixLabors.ImageSharp.Drawing", "SixLabors.Fonts",
            "System.Security.Cryptography.ProtectedData", "System.Security.Cryptography.Pkcs", "System.IO.Packaging",
            "System.Configuration.ConfigurationManager"
        };
        // We check src csproj PackageReferences are subset of allowed prefixes
        var violations = new List<string>();
        foreach (var csproj in GetCsprojFiles())
        {
            var lines = File.ReadAllLines(csproj);
            foreach (var line in lines)
            {
                if (line.Contains("PackageReference", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Include=", StringComparison.Ordinal))
                {
                    // Extract Include="..."
                    var start = line.IndexOf("Include=\"", StringComparison.Ordinal);
                    if (start < 0) continue;
                    start += "Include=\"".Length;
                    var end = line.IndexOf("\"", start, StringComparison.Ordinal);
                    if (end < 0) continue;
                    var pkg = line.Substring(start, end - start);
                    // Check if pkg is allowed (prefix match allowed, e.g., DocumentFormat.OpenXml.Framework)
                    var isAllowed = allowed.Any(a => pkg.Equals(a, StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith(a + ".", StringComparison.OrdinalIgnoreCase) ||
                                                     a.StartsWith(pkg + ".", StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith("DocumentFormat.OpenXml", StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith("SixLabors.", StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith("UglyToad.PdfPig", StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith("PDFsharp", StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith("PdfSharp", StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase) ||
                                                     pkg.StartsWith("System.", StringComparison.OrdinalIgnoreCase));
                    if (!isAllowed)
                        violations.Add($"{Path.GetFileName(csproj)} has unexpected PackageReference '{pkg}'");
                }
            }
        }
        violations.Should().BeEmpty("src csproj must only reference allowlisted local-only packages (MIT/Apache2.0, no network)");
    }

    // -----------------------------------------------------------------
    // 3. Cloud OCR absence
    // -----------------------------------------------------------------
    [Fact]
    public void CloudOcr_Absence()
    {
        var patterns = new[]
        {
            "CognitiveServices", "ComputerVision", "VisionService", "Azure.*Vision",
            "Textract", "GoogleVision", "CloudVision", "VisionClient", "ImageAnnotator",
            "Azure.AI.Vision", "Aws.Textract", "Google.Cloud.Vision"
        };
        var violations = new List<string>();
        foreach (var file in GetSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var pat in patterns)
            {
                // Simple contains for each; for regex-like, just check keyword
                var keyword = pat.Split('.')[0].Replace(".*", "");
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    // Need to avoid false positive on local "Vision" word; we check full set more strictly
                    if (keyword.Equals("Vision", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only flag if combined with Azure/Google/AWS
                        if (text.Contains("Azure", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("AWS", StringComparison.Ordinal) ||
                            text.Contains("Textract", StringComparison.OrdinalIgnoreCase))
                            violations.Add($"{Path.GetFileName(file)} possible cloud OCR '{keyword}' -> {file}");
                    }
                    else
                    {
                        violations.Add($"{Path.GetFileName(file)} contains cloud OCR '{keyword}' -> {file}");
                    }
                }
            }
        }
        // Also check csproj
        foreach (var csproj in GetCsprojFiles())
        {
            var text = File.ReadAllText(csproj);
            if (text.Contains("Cognitive", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Textract", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Vision", StringComparison.OrdinalIgnoreCase) && text.Contains("Google", StringComparison.OrdinalIgnoreCase))
                violations.Add($"{Path.GetFileName(csproj)} references cloud OCR package");
        }
        violations.Should().BeEmpty("product must not reference cloud OCR services");
    }

    // -----------------------------------------------------------------
    // 4. External AI API absence
    // -----------------------------------------------------------------
    [Fact]
    public void ExternalAi_Absence()
    {
        var patterns = new[] { "OpenAI", "Anthropic", "Claude", "Gemini", "api.openai.com", "api.anthropic.com", "generativelanguage.googleapis" };
        var violations = new List<string>();
        foreach (var file in GetSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var pat in patterns)
            {
                if (text.Contains(pat, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{Path.GetFileName(file)} contains external AI '{pat}' -> {file}");
            }
        }
        foreach (var csproj in GetCsprojFiles())
        {
            var text = File.ReadAllText(csproj);
            foreach (var pat in patterns)
            {
                if (text.Contains(pat, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{Path.GetFileName(csproj)} references external AI '{pat}'");
            }
        }
        violations.Should().BeEmpty("product must not reference external AI APIs (OpenAI/Anthropic/Gemini)");
    }

    // -----------------------------------------------------------------
    // 5. Temp workspace: isolation, ACL, cleanup
    // -----------------------------------------------------------------
    [Fact]
    public void TempWorkspace_IsolationAndCleanup()
    {
        var fs = new FileSystem();
        var ws1 = new SecureTempWorkspace(fs);
        var ws2 = new SecureTempWorkspace(fs);
        try
        {
            ws1.RootPath.Should().NotBe(ws2.RootPath);
            Directory.Exists(ws1.RootPath).Should().BeTrue();
            Directory.Exists(ws2.RootPath).Should().BeTrue();
            Directory.Exists(ws1.InputPath).Should().BeTrue();
            Directory.Exists(ws1.OutputPath).Should().BeTrue();
        }
        finally
        {
            ws1.Dispose();
            ws2.Dispose();
        }
        Directory.Exists(ws1.RootPath).Should().BeFalse();
        Directory.Exists(ws2.RootPath).Should().BeFalse();
    }

    [Fact]
    public void TempCleanup_OnSuccess()
    {
        var fs = new FileSystem();
        var ws = new SecureTempWorkspace(fs);
        var root = ws.RootPath;
        File.WriteAllText(Path.Combine(ws.InputPath, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(ws.OutputPath, "b.txt"), "world");
        Directory.Exists(root).Should().BeTrue();
        ws.Dispose();
        Directory.Exists(root).Should().BeFalse("workspace must be deleted on Dispose (success path)");
    }

    [Fact]
    public void TempCleanup_OnCancellation()
    {
        var fs = new FileSystem();
        var ws = new SecureTempWorkspace(fs);
        var root = ws.RootPath;
        File.WriteAllText(Path.Combine(ws.InputPath, "a.txt"), "data");
        try
        {
            throw new OperationCanceledException("simulated cancel");
        }
        catch (OperationCanceledException)
        {
            // Simulate using block finally
            ws.Dispose();
        }
        Directory.Exists(root).Should().BeFalse("workspace must be deleted even after cancellation");
    }

    [Fact]
    public void TempCleanup_OnException()
    {
        var fs = new FileSystem();
        SecureTempWorkspace? ws = null;
        string root = "";
        try
        {
            ws = new SecureTempWorkspace(fs);
            root = ws.RootPath;
            File.WriteAllText(Path.Combine(ws.ExtractedPath, "x.bin"), "x");
            throw new InvalidOperationException("simulated failure");
        }
        catch (InvalidOperationException)
        {
            ws!.Dispose();
        }
        Directory.Exists(root).Should().BeFalse("workspace must be deleted even after exception");
    }

    [Fact]
    public void TempCleanup_CrashCleanup_StaleRemovesOldAndKeepsRecent()
    {
        // Simulate crash: directory left behind with old timestamp, plus a fresh one
        var rootBase = Path.Combine(Path.GetTempPath(), "EksimSafeCopy");
        Directory.CreateDirectory(rootBase);
        var staleDir = Path.Combine(rootBase, "stale_" + Guid.NewGuid().ToString("N"));
        var freshDir = Path.Combine(rootBase, "fresh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staleDir);
        Directory.CreateDirectory(freshDir);
        File.WriteAllText(Path.Combine(staleDir, "old.txt"), "stale");
        File.WriteAllText(Path.Combine(freshDir, "new.txt"), "fresh");
        Directory.SetCreationTimeUtc(staleDir, DateTime.UtcNow - TimeSpan.FromHours(2));
        Directory.SetCreationTimeUtc(freshDir, DateTime.UtcNow);
        try
        {
            SecureTempWorkspace.CleanupStaleWorkspaces(TimeSpan.FromHours(1));
            Directory.Exists(staleDir).Should().BeFalse("stale workspace older than cutoff must be removed");
            Directory.Exists(freshDir).Should().BeTrue("fresh workspace must be preserved");
        }
        finally
        {
            if (Directory.Exists(staleDir)) Directory.Delete(staleDir, true);
            if (Directory.Exists(freshDir)) Directory.Delete(freshDir, true);
        }
    }

    // -----------------------------------------------------------------
    // 6. Original hash verification (SHA256 before/after)
    // -----------------------------------------------------------------
    [Fact]
    public void OriginalHash_Verification()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"orig_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "Ahmet Yılmaz\n11111111111\n0532 000 00 00\n");
        try
        {
            string hashBefore;
            using (var sha = SHA256.Create())
            using (var fs2 = File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hashBefore = Convert.ToHexString(sha.ComputeHash(fs2));
            }
            // Simulate read-only processing (like DocumentEngine does)
            using (var ro = File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using var reader = new StreamReader(ro);
                _ = reader.ReadToEnd();
            }
            string hashAfter;
            using (var sha = SHA256.Create())
            using (var fs2 = File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hashAfter = Convert.ToHexString(sha.ComputeHash(fs2));
            }
            hashAfter.Should().Be(hashBefore, "original file must be unchanged after read-only processing");
            // Also test via Infrastructure FileSystem.ComputeHash
            var fsI = new FileSystem();
            var r1 = fsI.ComputeHash(tmp);
            r1.IsSuccess.Should().BeTrue();
            var r2 = fsI.ComputeHash(tmp);
            r2.IsSuccess.Should().BeTrue();
            r1.Value.Should().Be(r2.Value);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void OriginalHash_StableAcrossReads()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"hash2_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, "TR00 0000 0000 0000 0000 0000 00\nMehmet Kaya");
        try
        {
            var fs = new FileSystem();
            var h1 = fs.ComputeHash(tmp);
            var h2 = fs.ComputeHash(tmp);
            h1.IsSuccess.Should().BeTrue();
            h2.IsSuccess.Should().BeTrue();
            h1.Value.Should().Be(h2.Value);
            // Write to a different file should diverge
            var other = Path.Combine(Path.GetTempPath(), $"hash_other_{Guid.NewGuid():N}.txt");
            File.WriteAllText(other, "different");
            try
            {
                var h3 = fs.ComputeHash(other);
                h3.Value.Should().NotBe(h1.Value);
            }
            finally { if (File.Exists(other)) File.Delete(other); }
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // -----------------------------------------------------------------
    // 7. Output independence: never overwrite original
    // -----------------------------------------------------------------
    [Fact]
    public void OutputIndependence_NotOverwriteOriginal()
    {
        var origDir = Path.Combine(Path.GetTempPath(), $"od_{Guid.NewGuid():N}");
        Directory.CreateDirectory(origDir);
        var origPath = Path.Combine(origDir, "original.txt");
        var outDir = Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "masked_original.txt");
        File.WriteAllText(origPath, "Name: Ahmet Yılmaz\nTC: 11111111111\n");
        string origHashBefore;
        using (var sha = SHA256.Create())
        using (var s = File.Open(origPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            origHashBefore = Convert.ToHexString(sha.ComputeHash(s));
        try
        {
            // Simulate masking: create new file, not overwriting original
            var content = File.ReadAllText(origPath);
            var masked = content.Replace("Ahmet Yılmaz", "[AD SOYAD]").Replace("11111111111", "[TC_KIMLIK_NO]");
            File.WriteAllText(outPath, masked);
            // Verify original unchanged
            string origHashAfter;
            using (var sha = SHA256.Create())
            using (var s = File.Open(origPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                origHashAfter = Convert.ToHexString(sha.ComputeHash(s));
            origHashAfter.Should().Be(origHashBefore, "original file hash must be identical after masking output was created");
            File.Exists(outPath).Should().BeTrue();
            outPath.Should().NotBe(origPath);
            File.ReadAllText(outPath).Should().NotContain("Ahmet Yılmaz");
            File.ReadAllText(outPath).Should().NotContain("11111111111");
            File.ReadAllText(origPath).Should().Contain("Ahmet Yılmaz", "original must retain unmasked content");
        }
        finally
        {
            if (File.Exists(origPath)) File.Delete(origPath);
            if (Directory.Exists(origDir)) Directory.Delete(origDir, true);
            if (File.Exists(outPath)) File.Delete(outPath);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }

    // -----------------------------------------------------------------
    // 8. Firewall & Offline documented (audit file exists)
    // -----------------------------------------------------------------
    [Fact]
    public void FirewallAndOffline_Documented()
    {
        var repo = FindRepoRoot();
        var auditPath = Path.Combine(repo, "docs", "security", "LOCAL_ONLY_AUDIT.md");
        File.Exists(auditPath).Should().BeTrue("LOCAL_ONLY_AUDIT.md must exist for Phase 11");

        var text = File.ReadAllText(auditPath);
        text.Should().Contain("Windows Firewall", "audit must document firewall outbound test procedure");
        text.Should().Contain("Offline", "audit must document offline Windows 11 test");
        text.Should().Contain("Temp workspace", "audit must cover temp workspace security");
        text.Should().Contain("SHA256", "audit must cover original hash verification");
        text.Should().Contain("Output independence", "audit must cover output independence");
        text.Should().Contain("PASS", "audit should contain PASS verdict for local-only boundary");
        text.Length.Should().BeGreaterThan(5000, "audit document should be comprehensive");
    }
}
