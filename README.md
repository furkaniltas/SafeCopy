# SafeCopy

Secure PII Redaction — Local First. A Windows 11 desktop application that detects and irreversibly masks personally identifiable information (PII) before documents are shared with external services. Fully offline, no cloud APIs, no telemetry.

## Features

- **Fully local operation** — no outbound network calls, no cloud OCR, no external AI APIs
- **Deterministic masking** — original file never modified; output is a new file
- **Output verification** — output is re-scanned; delivery blocked if residual PII remains
- **Temporary workspace isolation** — per-session GUID directory with ACL and deterministic cleanup
- **Batch processing** — queue, progress, retry, and cancellation across multiple files
- **Turkish PII focus** with extensible detector pipeline

## Supported Detection Types

| Category | Types |
|---|---|
| Identity | `TcKimlikNo`, `TaxId`, `PassportNo`, `SgkNo` |
| Contact | `Phone`, `Email`, `Address`, `FullName`, `FirstName`, `LastName`, `MotherName`, `FatherName`, `Username` |
| Financial | `Iban`, `CreditCard`, `CardExpiry`, `Cvv` |
| Network | `IpAddress`, `Url`, `MacAddress` |
| Utilities | `TesisatNo`, `AboneNo`, `SayacNo` |
| Legal / Customer | `MusteriNo`, `DosyaNo`, `DavaNo`, `LicensePlate`, `Date`, `BloodType` |
| Platform | `Secret`, `PossiblePersonalData` (heuristic), structured XLSX field types |

Detectors are independent components (`TcKimlikDetector`, `PhoneDetector`, `EmailDetector`, `IbanDetector`, `PersonNameDetector`, `AddressDetector`, `InstallationNumberDetector`/Tesisat, etc.) flowing through a common `Detection` model. Results include type, value, context, confidence, bounding box, and page number.

Secret detection patterns (e.g., `FALCON_CLIENT_SECRET`) are technical and retained as-is.

## Supported Formats

- **PDF** — ingestion via PdfPig; redaction currently unsupported via fallback (fails securely) due to true content-stream redaction limits with PdfSharp
- **DOCX** — DocumentFormat.OpenXml ingestion and redaction
- **XLSX** — DocumentFormat.OpenXml ingestion and redaction including structured detectors
- **TXT** — plain text ingestion and redaction
- **UDF** — custom delimited format ingestion and redaction
- **Images** — PNG/JPEG etc. via ImageSharp, with optional local OCR pre-processing

All ingestion adapters produce the Common Document Model (`Document → DocumentPage → TextBlock → TextSpan`) — UI never depends on format internals.

Flow: `Input → Document Adapter → Common Document Model → Detection Engine → Redaction/Planner → Renderer → Verification`

## Security Model

- **Local-only**: verified zero outbound network calls; no HTTP/DNS usage at runtime; Windows.Media.Ocr reflection only (OS-bundled, no network)
- **Temp workspace**: `%TEMP%\SafeCopy\{guid}` per session, ACL-restricted, deterministic cleanup on dispose/finalizer, stale-workspace reaper (`CleanupStaleWorkspaces`)
- **Original immutability**: SHA256 hash captured on load and verified; original file never overwritten
- **Output verification**: `VerificationEngine` reloads output via `DocumentEngine` and re-runs detectors; residual PII → verification failure, no "Safe Copy Ready"
- **Manual validation**: documented offline test and Windows Firewall outbound block test (see `docs/security/LOCAL_ONLY_AUDIT.md`)

See `docs/security/` for full audits:
- `LOCAL_ONLY_AUDIT.md` — local-only guarantees, ACL, firewall/offline checks
- `THREAT_MODEL.md` — threat model
- `TEMP_WORKSPACE_SECURITY.md` — temp isolation
- `ORIGINAL_IMMUTABILITY.md` — immutability guarantee
- `OUTPUT_VERIFICATION.md` — mandatory verification
- `OCR_ATTACK_SURFACE.md`, `ZIP_XML_ATTACK_SURFACE.md`

## Installation (.NET 10)

**Prerequisites:** Windows 11, .NET 10 SDK, `net10.0-windows` with WPF.

```powershell
# Clone
git clone <repo-url> SafeCopy
cd SafeCopy

# Restore & build
dotnet build -c Release
# or: MSBuild.exe SafeCopy.slnx /t:Build /p:Configuration=Release

# Run
dotnet run --project src/SafeCopy.App/SafeCopy.App.csproj -c Release
# Executable: SafeCopy.exe (AssemblyName SafeCopy, version 1.0.0.0)
```

Publish example (verified outside repo):
```powershell
dotnet publish src/SafeCopy.App/SafeCopy.App.csproj -c Release -o C:\Temp\SafeCopyRelease
```

Assets: `src/SafeCopy.App/Assets/Icons/SafeCopy.ico`

## Usage

1. Launch SafeCopy (Title: **SafeCopy**, Subtitle: *Secure PII Redaction — Local First*).
2. **Dosya Seç** — pick a supported file.
3. Review **Tespitler** (definite) and collapsible **Olası Kişisel Veriler** (medium confidence) — toggle selection.
4. Choose **Maskeleme** mode: `FullRedaction`, `Placeholder`/`TypeLabel`, `PartialMask`.
5. **Güvenli Kopya Oluştur** — output is written to a new file; original unchanged.
6. Check **Doğrulama** panel: `✓ Güvenli kopya hazır` (pass) vs `✗ Güvenli kopya hazır değil` (blocked). **Çıktıyı Aç** opens the safe copy.
7. **Toplu İşlem Kuyruğu** supports drag & drop, multi-select, batch start, cancel, retry, clear; status shows `Başarılı / Başarısız / Desteklenmiyor / İptal`.

OCR activates only when needed (scanned PDF, images), Turkish + English support via local `IOcrEngine`.

## Verification

Mandatory pipeline:

```
Output → Reload via DocumentEngine → Re-run Detectors → Residual PII?
  → 0 residual: Passed = true, "Güvenli kopya hazır"
  → >0 residual (critical types like TcKimlikNo/Iban): Passed = false, blocked
```

No safe-copy message without a verification pass. Metadata and hidden content are also checked/sanitized.

## Privacy

- No internet, no cloud OCR, no ChatGPT/Claude/Gemini/Copilot, no telemetry, no analytics, no remote config/logging, no CDN/fonts.
- Processing stays on-device; temp files under `%TEMP%\SafeCopy` and removed on clean exit; can be purged manually.
- Only synthetic/anonymized test data is used (e.g., `Ahmet Yılmaz`, `11111111111`, `0532 000 00 00`, `TR00 0000 0000 0000 0000 0000 00`). Never commit real citizen data, `bin/`, `obj/`, `.vs/`, `TestResults/`, `temp/`, or PII.

## Limitations

- **PDF redaction** via PdfSharp 6.2.0 cannot reliably remove content streams (compressed streams, XObjects, Tj/TJ, incremental updates, annotations) without leaving extractable text → fails securely and reports unsupported rather than producing a false-safe PDF. Convert to DOCX/TXT/XLSX/Image or apply manual redaction for PDF PII.
- **OCR** depends on OS availability (`Windows.Media.Ocr` via reflection); `IsAvailable`, `EngineName`, `SupportedLanguages ["tr","en"]`, `SecureImagePreprocessor` limits (MaxDimension 8192, MaxPixels 100 MP, MaxFileSize 50 MB, deskew/denoise/contrast).
- **UDF** is a project-specific delimited format.
- Deterministic string/bitmap masking; image redaction overlays bounding boxes.

## Roadmap

- True PDF content-stream redaction via audited commercial or pure-managed library
- Additional Turkish PII types and structured XLSX coverage
- Expanded OCR language and image pre-processing
- Installer (MSIX/MSI) offline distribution

## Contributing

See `CONTRIBUTING.md`. Keep clean architecture (Core has no WPF/file-system/Windows API), run build + tests, use synthetic data only, and follow `AGENTS.md` phase gates.

## Security

See `SECURITY.md` for reporting, boundaries, and verification. For audits, see `docs/security/`.

## License

MIT License — see `LICENSE`. Copyright (c) 2026 SafeCopy.

SafeCopy is released under the MIT License, permitting use, copy, modification, distribution, and commercial use under the terms in `LICENSE`.

