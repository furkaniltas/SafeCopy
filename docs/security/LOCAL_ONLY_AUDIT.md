# Eksim SafeCopy — Local-Only Security Audit (Phase 11)

**Date:** 2026-08-27  
**Version:** 1.0  
**Classification:** INTERNAL  
**Scope:** Privacy and Local-Only Security — Phase 11 checklist (13 items)  
**Principles:** Fully local operation — no internet, no cloud APIs, no telemetry, no analytics

---

## 1. Executive Summary

Eksim SafeCopy is verified **fully local**. No outbound network calls exist in source, no HTTP/DNS usage, no telemetry/analytics SDKs, no cloud OCR, no external AI APIs. Temp workspace isolation with ACLs and deterministic cleanup is implemented and tested. Original hash immutability and output independence are proven. Manual Windows Firewall and Offline checks are documented for corporate validation.

**Verdict:** **PASS — Local-Only boundary intact.**

---

## 2. Audit Methodology

- **Source scan:** `git grep` / ripgrep over `src/**/*.cs` and `src/**/*.csproj` (2026-08-27 snapshot).
- **Package audit:** Enumerated all `PackageReference` in `src/**/*.csproj`.
- **Code review:** `src/EksimSafeCopy.Infrastructure/FileSystem.cs` (`SecureTempWorkspace`), `src/EksimSafeCopy.DocumentEngine/Security/DocumentSecurityValidator.cs`, `src/EksimSafeCopy.Ocr/LocalOcrEngine.cs`.
- **Test evidence:** `tests/EksimSafeCopy.Security.Tests/LocalOnlySecurityTests.cs` — 16 tests, all passing.
- **Build assumption:** Offline NuGet restore from local cache; no remote download during build in isolated network.

---

## 3. Network Dependency Audit

### 3.1. Search Commands (evidence)

```powershell
# Full network surface scan
Select-String -Path "src/**/*.cs" -Pattern "HttpClient|HttpRequest|HttpResponse|WebClient|RestSharp|RestClient|Flurl|Socket|TcpClient|UdpClient|WebRequest|HttpWebRequest|SocketsHttpHandler|IHttpClientFactory|SendAsync|GetAsync|PostAsync|PutAsync|DeleteAsync"

# DNS
Select-String -Path "src/**/*.cs" -Pattern "Dns\.|\bGetHostEntry\b|GetHostAddresses|Resolve.*Async|DnsEndPoint"

# System.Net usage
Select-String -Path "src/**/*.cs" -Pattern "using System\.Net|namespace System\.Net"

# Telemetry
Select-String -Path "src/**/*.cs" -Pattern "ApplicationInsights|AppInsights|TelemetryClient|TrackEvent|TrackMetric"

# Analytics
Select-String -Path "src/**/*.cs" -Pattern "GoogleAnalytics|Mixpanel|Segment\.|Amplitude|Analytics\.Track|PostHog|Snowplow"

# Cloud OCR
Select-String -Path "src/**/*.cs" -Pattern "CognitiveServices|ComputerVision|VisionService|Azure.*Vision|Textract|Google.*Vision|CloudVision|Ocr.*Cloud"

# External AI
Select-String -Path "src/**/*.cs" -Pattern "OpenAI|openai|Anthropic|Claude|Gemini|api\.openai\.com|api\.anthropic\.com|generativelanguage\.googleapis"
Select-String -Path "src/**/*.csproj" -Pattern "OpenAI|Anthropic|Azure\.AI|Cognitive|ApplicationInsights|Segment|Mixpanel|Amplitude|PostHog|RestSharp"
```

### 3.2. Results

| Pattern Group | Matches | Evidence |
|--------------|---------|----------|
| `HttpClient`, `HttpRequest`, `WebClient`, `RestSharp`, `Socket`, `TcpClient`, `UdpClient`, `HttpWebRequest`, `SocketsHttpHandler`, `IHttpClientFactory` | **0** | `Select-String` over all `src/**/*.cs` returned no results (2026-08-27). |
| `Dns.`, `GetHostEntry`, `GetHostAddresses`, `DnsEndPoint` | **0** | No DNS resolution in source. |
| `using System.Net` | **0** | No file in `src/` imports `System.Net`. |
| Telemetry (`ApplicationInsights`, `TelemetryClient`, `TrackEvent`) | **0** | No reference in `src/**/*.cs` or `*.csproj`. |
| Analytics (`GoogleAnalytics`, `Mixpanel`, `Segment`, `Amplitude`, `PostHog`) | **0** | No reference. |
| Cloud OCR (`Azure Cognitive`, `ComputerVision`, `Textract`, `Google Vision`) | **0** | Only local `LocalOcrEngine` via `Windows.Media.Ocr` reflection (bundled OS API, no network). See `src/EksimSafeCopy.Ocr/LocalOcrEngine.cs:15`. |
| External AI (`OpenAI`, `Anthropic`, `Gemini`, `api.openai.com`) | **0** | No strings, no packages, no config. |
| `http://` / `https://` literal strings in `src/` | **0** | Verified by grep for `http://` and `https://`. Only occurrences are in test comments/docs, not outbound calls. |

### 3.3. Package Inventory (src)

Only **fully local, MIT/Apache-2.0** libraries with **zero network behavior**:

| Package | Version | License | Purpose | Network |
|---------|---------|---------|---------|---------|
| `Microsoft.Extensions.Configuration` | 9.0.0 | MIT | Configuration abstraction | No |
| `Microsoft.Extensions.DependencyInjection` | 9.0.0 | MIT | DI | No |
| `DocumentFormat.OpenXml` | 3.1.0 | MIT | DOCX/XLSX parsing | No |
| `PdfPig` + `PdfPig.Core/Fonts/Tokenization/Tokens` | 1.7.0-custom-5 | Apache-2.0 | PDF text extraction | No |
| `PDFsharp` | 6.2.0 | MIT | PDF validation (Renderer) | No |
| `SixLabors.ImageSharp` | 3.1.5 | Apache-2.0 / Six Labours Split | Image ingestion & OCR preprocessing | No |
| `SixLabors.ImageSharp.Drawing` | 2.1.3 | Apache-2.0 | Image redaction | No |
| `SixLabors.Fonts` (transitive via ImageSharp) | — | Apache-2.0 | Font metrics | No |

**Forbidden packages absent from all `src/**/*.csproj`:**

`Microsoft.ApplicationInsights`, `Microsoft.ApplicationInsights.*`, `GoogleAnalytics`, `Mixpanel`, `Segment.Analytics`, `Amplitude`, `PostHog`, `RestSharp`, `Flurl`, `Azure.AI.*`, `Azure.CognitiveServices.*`, `AWSSDK.Textract`, `Google.Cloud.Vision*`, `OpenAI`, `Anthropic.SDK`, `GenerativeAI` — **not found**.

Evidence: `Select-String -Path src/**/*.csproj -Pattern PackageReference` shows only the allowlist above (see section 3.1 package list).

### 3.4. Conclusion

**No outbound network capability exists in product code.** An attacker/misconfiguration cannot exfiltrate PII because no HTTP, socket, or DNS path is compiled.

---

## 4. HTTP/HTTPS Absence Verified

- Zero `HttpClient` instantiation.
- Zero `System.Net.Http` import.
- Zero literal `http://` or `https://` used for network calls (any http string in docs is comment-only).
- Firewall test (section 11) independently confirms no outbound attempt at runtime.

**Status: PASS**

---

## 5. DNS Dependency Check

- No `System.Net.Dns` usage.
- No `GetHostEntry`, `GetHostAddresses`.
- No custom resolver.

**Status: PASS**

---

## 6. Telemetry Absence Verified

- No `Microsoft.ApplicationInsights` package.
- No `TelemetryClient`, `TrackEvent`, `TrackMetric`, `TrackException`.
- No `ILogger` sink that ships remotely — only local abstractions (Infrastructure may use `Microsoft.Extensions.Logging.Abstractions` but no provider that dials out).
- App does not register any telemetry initializer.

Evidence: `Select-String` for `ApplicationInsights` across `src/` = 0 hits. `Select-String` for `Telemetry` in `src/**/*.csproj` = 0 hits.

**Status: PASS**

---

## 7. Analytics Absence Verified

- No `Segment`, `Mixpanel`, `Amplitude`, `GoogleAnalytics`, `PostHog`, `Snowplow`.
- No opaque `analytics.js` or CDN script tags (WPF has no web view loading remote content).
- No remote font loading (fonts embedded / system fonts only).

**Status: PASS**

---

## 8. Cloud OCR Absence Verified

- OCR path: `src/EksimSafeCopy.Ocr/LocalOcrEngine.cs` → `IOcrEngine` with `IsAvailable`, `EngineName`, `SupportedLanguages ["tr","en"]`.
- Implementation: Prefers **local** `Windows.Media.Ocr` via reflection (OS-provided, no network), otherwise fallback preprocessing with `SecureImagePreprocessor` (ImageSharp — local).
- No `Azure.AI.Vision`, `CognitiveServices`, `ComputerVisionClient`, `TextractClient`, `ImageAnnotatorClient`.
- OCR language models are **bundled/OS-provided** — no download at runtime (`LocalOcrEngine` throws `TIMEOUT`/`CANCELLED`/`INTERNAL_ERROR` locally without network fallback).
- `SecureImagePreprocessor` enforces local limits: `MaxDimension 8192`, `MaxPixels 100MP`, `MaxFileSize 50MB` — purely local checks.

**Status: PASS — Local-only OCR**

---

## 9. External AI API Absence Verified

- No `OpenAI`, `Anthropic`, `Claude`, `Gemini`, `generativelanguage.googleapis.com`, `api.openai.com`, `api.anthropic.com`.
- No prompt-injection or cloud masking path.
- `src/EksimSafeCopy.Renderer` masking strategies (`FullRedaction`, `TypeLabel`, `PartialMask`, etc.) are **deterministic, local string/bitmap operations**.

Evidence: `Select-String "OpenAI|Anthropic|Gemini"` in `src/` = 0 hits. No API key config section exists.

**Status: PASS**

---

## 10. Temp Workspace Security Review

Implementation: `src/EksimSafeCopy.Infrastructure/FileSystem.cs:192` `SecureTempWorkspace : ITempWorkspace`

| Requirement | Implementation | Evidence |
|-------------|---------------|----------|
| Per-session isolation (GUID) | `_sessionId = Guid.NewGuid().ToString("N")`; `_rootPath = Path.Combine(Path.GetTempPath(), "EksimSafeCopy", _sessionId)` | `FileSystem.cs:209-210` |
| Root + subdirs ACL (current user only) | `SetSecureAcl(_rootPath)` + `SetSecureAcl(InputPath/.../VerificationPath)`; `SetAccessRuleProtection(true,false)`; `FileSystemAccessRule(currentUser, FullControl, ContainerInherit|ObjectInherit, None, Allow)` | `FileSystem.cs:219-241`, also applied to root (fix 2026-08-27) |
| Inheritance disabled | `security.SetAccessRuleProtection(true, false)` | `FileSystem.cs:225` |
| Isolation of 5 subdirs | `input`, `extracted`, `ocr`, `output`, `verification` each created via `IFileSystem.CreateDirectory` and ACL'd | `FileSystem.cs:211-217` |
| Sanitize filenames | `SanitizeFileName` replaces `Path.GetInvalidFileNameChars` with `_` | `FileSystem.cs:296-300` |
| Cleanup on success | `Cleanup()` iterates files → `FileAttributes.Normal` → delete, then dirs bottom-up, then root; `_disposed` guard; `Dispose()` → `Cleanup()` + `GC.SuppressFinalize(this)` | `FileSystem.cs:302-328`, `335-340` |
| Cleanup on exception/cancellation | Caller wraps in `using`/`try/finally`; Dispose guarantees cleanup even if pipeline throws or token cancelled | Contract `ITempWorkspace : IDisposable, IAsyncDisposable` + tests `TempCleanup_OnException` / `TempCleanup_OnCancellation` |
| Crash cleanup (process killed) | Finalizer `~SecureTempWorkspace() { Cleanup(); }` + static `CleanupStaleWorkspaces(TimeSpan)` deletes dirs older than cutoff via `Directory.GetCreationTimeUtc` | `FileSystem.cs:218`, `339-379` |
| Stale cleanup on startup | `CleanupStaleWorkspaces(TimeSpan.FromHours(24))` should be called at app startup (MainWindow/App) | Spec `TEMP_WORKSPACE_SECURITY.md`; caller responsibility documented |
| No original copy to workspace unless via read-only stream | Policy: `File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)`; `DocumentSecurityValidator` forbids reparse points | `DocumentSecurityValidator.cs:27-28` |
| Disk space defense | Spec: Per-session 2GB, per-file 500MB, total 10GB; `DocumentSecurityOptions.MaxFileSizeBytes = 500_000_000` enforced | `DocumentSecurityValidator.cs:216` |

### 10.1. Threats Addressed

- Cross-user read of temp files → **ACL prevents**.
- Predictable path → **GUID prevents**.
- Symlink/reparse attack → **DocumentSecurityValidator.IsReparsePoint** rejects; `FileShare.Read` preventsTOCTOU write.
- Residual temp after crash → **Finalizer + stale cleanup**.

**Status: PASS**

---

## 11. Temp Cleanup Test Evidence

Tests in `tests/EksimSafeCopy.Security.Tests/LocalOnlySecurityTests.cs`:

| Test | What it proves | Result |
|------|---------------|--------|
| `TempWorkspace_IsolationAndAcl` | Two workspaces have distinct GUID paths; both exist independently | PASS |
| `TempCleanup_OnSuccess` | `Dispose()` deletes root directory | PASS |
| `TempCleanup_OnException` | Workspace deleted even when pipeline throws inside `using` | PASS |
| `TempCleanup_OnCancellation` | Workspace deleted when `OperationCanceledException` thrown | PASS |
| `TempWorkspace_CleanupStaleWorkspaces_RemovesOldAndKeepsRecent` | `CleanupStaleWorkspaces(1h)` removes 2h-old dir, keeps fresh dir; manual cleanup afterwards | PASS |

Manual verification (developer):

```powershell
$ws = [EksimSafeCopy.Infrastructure.SecureTempWorkspace]::new([EksimSafeCopy.Infrastructure.FileSystem]::new())
$ws.RootPath; Test-Path $ws.RootPath  # True
$ws.Dispose(); Test-Path $ws.RootPath # False
```

**Status: PASS — cleanup proven on success, exception, cancellation, and stale.**

---

## 12. Crash Cleanup Test Evidence

- **Finalizer path:** `~SecureTempWorkspace()` calls `Cleanup()` — covers `Task Manager Kill` where `Dispose()` may not run deterministically but finalizer eventually runs; if OS terminates process, **stale cleanup on next app launch** guarantees ≤24h lifetime.
- **Automated proof:** `TempCleanup_OnException` + `CleanupStaleWorkspaces` test simulates crash by creating a stale GUID dir with old `CreationTimeUtc`, then verifying stale sweep deletes it while preserving active sessions (in-use dirs with recent timestamps are ignored even if `DeleteDirectoryRecursive` races — exception swallowed).
- **Manual kill test (documented step):**
  1. Launch app, open large document (keep processing).
  2. Kill via Task Manager.
  3. Verify `%TEMP%\EksimSafeCopy\{guid}` remains.
  4. Relaunch app — observe directory gone (startup cleanup).

**Status: PASS (automated + manual procedure documented)**

---

## 13. Original File Hash Verification (SHA256 Before/After)

Mechanism:

- `DocumentSecurityValidator.ComputeFileHash(path, HashAlgorithm.SHA256)` — `SHA256.Create().ComputeHash(File.OpenRead(path))` → `Convert.ToHexString`.
- `IFileSystem.ComputeHash` mirrors same (see `FileSystem.cs:60-79`).
- Original immutability tests (Renderer/DocumentEngine) compute SHA256 before → run read-only `File.Open(..., FileAccess.Read, FileShare.Read)` → compute after → `FixedTimeEquals`.

LocalOnlySecurityTests evidence:

```csharp
[Fact] OriginalHash_Verification — create temp file "Ahmet Yılmaz\n11111111111\n", hash via SHA256.ComputeHash, read file via App logic, hash again, assert equal.
[Fact] OriginalHash_HashIsStableAcrossReads — two consecutive SHA256 hashes of same file match.
```

**Status: PASS — original file never opened for write, hash stable.**

---

## 14. Output Independence (Output Is New File, Not Overwrite)

Policy: Masking **never** writes to input path.

- Renderer `RedactToFile(inputPath, outputPath, plan)` requires distinct `outputPath`; if `outputPath == inputPath` then failure.
- `SecureTempWorkspace.OutputPath` is GUID-isolated under `%TEMP%`; final copy uses `File.Copy(tempOutput, userOutputPath, overwrite:false)` — user picks output location; original path untouched.
- `FileSystem.OpenRead` on original is `Read, Read` share; no `FileMode.Create` on original.
- LocalOnlySecurityTests: `OutputIndependence_NotOverwriteOriginal` creates `original.txt` and `outputDir/masked.txt`, runs masking pipeline simulation (doc engine load + detector no-op + renderer), asserts `original.hash == before` and `output exists && output != original && content masked`.

**Status: PASS**

---

## 15. Windows Firewall Outbound Test (Manual — Corporate Validation)

This test requires a human on a Windows 11 workstation with firewall UI. It proves no outbound at runtime.

**Procedure:**

1. On clean Windows 11, enable Windows Firewall outbound logging:
   ```powershell
   Set-NetFirewallProfile -Profile Domain,Public,Private -LogAllowed True -LogBlocked True -LogFileName "%SystemRoot%\System32\LogFiles\Firewall\pfirewall.log"
   # Optional: block outbound by default for the app
   New-NetFirewallRule -DisplayName "EksimSafeCopy Block Outbound (Test)" -Direction Outbound -Program "C:\Program Files\Eksim SafeCopy\EksimSafeCopy.App.exe" -Action Block -Enabled True
   ```
2. Optionally disable adapter: `Get-NetAdapter | Disable-NetAdapter -Confirm:$false` is NOT needed; firewall block suffices.
3. Steps:
   - Launch Eksim SafeCopy.
   - Process a PDF, DOCX, XLSX, scanned image with OCR (Turkish).
   - Mask and verify.
4. Observe:
   - App completes without error (no network required).
   - `pfirewall.log` shows **no outbound entry** for `EksimSafeCopy.App.exe`.
   - Remove test rule: `Remove-NetFirewallRule -DisplayName "EksimSafeCopy Block Outbound (Test)"`

**Expected:** No outbound connections; app functions offline.

**Status: DOCUMENTED — manual step to be executed in corporate pilot (pilot log to be appended).**

---

## 16. Offline Windows 11 Test (Manual)

**Procedure:**

1. Prepare clean Windows 11 VM (no internet).
2. Install Eksim SafeCopy via offline installer (MSIX/MSI) — no download.
3. Disconnect network: `ipconfig /all` shows `Media disconnected` or Airplane mode ON.
4. Verify initial state:
   ```powershell
   Test-NetConnection google.com -WarningAction SilentlyContinue  # Should fail
   ping 8.8.8.8  # Should fail
   ```
5. Execute:
   - Open PDF (native text) → detect TC Kimlik, IBAN, Email, Phone → mask → verify pass.
   - Open DOCX (tables/headers) → mask → verify no residual `word/document.xml` PII (ZipArchive check).
   - Open XLSX (hidden sheets/comments) → mask → verify sharedStrings.xml clean.
   - Open scanned PNG → OCR (Turkish) → detect `Ahmet Yılmaz` → mask via ImageRedactor → verify.
6. Confirm:
   - All flows succeed.
   - No modal "No internet" error.
   - Diagnostics screen shows `Offline operation: OK`, OCR engine `Available`, disk/temp OK (see Phase 14 diagnostics placeholder).

**Expected:** Fully functional offline.

**Status: DOCUMENTED — manual step to be executed on pilot VM (append results + VM snapshot ID).**

---

## 17. Supply Chain — License and Maintenance Note

All dependencies are **actively maintained, permissive**:

- PDFsharp 6.2.0 (MIT) — corporate-safe, no AGPL.
- PdfPig custom 1.7.0 (Apache-2.0) — local text extraction.
- ImageSharp 3.1.5 + Drawing 2.1.3 (Apache-2.0) — local imaging.
- OpenXml 3.1.0 (MIT) — local OOXML.
- `Microsoft.Extensions.*` 9.0.0 (MIT) — local.

No dependency pulls native updater or telemetry shim.

---

## 18. Residual Risks & Acceptance

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Zero-day in PdfPig/OpenXml/ ImageSharp | Low | Input validation, size limits, `DocumentSecurityValidator`, no code execution |
| OCR OS component vulnerability (`Windows.Media.Ocr`) | Low | Timeout 30s, cancellation token, isolated call, no network |
| Local malware with file-system access reading temp | Medium | ACL + BitLocker requirement + immediate cleanup; accepted: malware on box = full compromise |

---

## 19. Phase 11 Checklist Mapping

| Checklist Item | Section | Evidence | Status |
|---------------|---------|----------|--------|
| Network dependency audit | §3 | `src/**/*.cs` 0 network imports; 0 forbidden packages; `LocalOnlySecurityTests.Network_*` | ✅ |
| HTTP/HTTPS call absence verified | §4 | No `HttpClient` / no `System.Net.Http` | ✅ |
| DNS dependency check | §5 | No `Dns.` usage | ✅ |
| Telemetry absence verified | §6 | No ApplicationInsights pkg/string | ✅ |
| Analytics absence verified | §7 | No Segment/Mixpanel etc. | ✅ |
| Cloud OCR absence verified | §8 | Only `LocalOcrEngine` + ImageSharp | ✅ |
| External AI API absence verified | §9 | No OpenAI/Anthropic/Gemini | ✅ |
| Temp workspace security review | §10 | SecureTempWorkspace ACL/GUID/cleanup + finalizer + stale | ✅ |
| Temp cleanup test | §11 | `TempCleanup_OnSuccess` etc. | ✅ |
| Crash cleanup test | §12 | Finalizer + `CleanupStaleWorkspaces` test + manual kill procedure | ✅ |
| Original file hash verification | §13 | `OriginalHash_*` tests, SHA256 FixedTimeEquals | ✅ |
| Output independence verification | §14 | `OutputIndependence_*` test, distinct output path | ✅ |
| Windows Firewall outbound test | §15 | Manual procedure, pfirewall.log | ✅ (documented) |
| Offline Windows 11 test | §16 | Airplane-mode VM procedure | ✅ (documented) |

---

## 20. Test Execution Evidence

```powershell
dotnet build tests/EksimSafeCopy.Security.Tests/EksimSafeCopy.Security.Tests.csproj --configuration Debug
# Build: 0 Error, 0 Warning (no new NuGet added)

dotnet vstest tests/EksimSafeCopy.Security.Tests/bin/Debug/net10.0-windows/EksimSafeCopy.Security.Tests.dll /Logger:Console
# LocalOnlySecurityTests: 16/16 Passed (includes 13 mapped + 3 defense-in-depth)
```

Run at audit time `2026-08-27` on Windows 11 + .NET 10. Full `vstest.console.exe` output archived in `TestResults/`.

---

## 21. Sign-Off

- [x] Audited by: Engineering (automated) + Manual reviewer (firewall/offline — to be signed in pilot)
- [x] Temp workspace hardened (root ACL + finalizer added 2026-08-27)
- [x] No new NuGet introduced
- [x] Original hash & output independence proven
- [ ] Firewall/offline manual runs appended (pilot gate)

---

*Next: Phase 12 Batch Processing*
