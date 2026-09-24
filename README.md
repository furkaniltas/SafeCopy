# SafeCopy

Secure PII Redaction — Local First. A Windows 11 desktop application that detects and masks personally identifiable information (PII) before documents are shared with external services.

## Features

- **Fully local operation** — no outbound network calls, no cloud OCR, no external AI APIs
- **Deterministic masking** — original file never modified; output is a new file
- **Output verification** — output is re-scanned; delivery blocked if residual PII remains
- **Temporary workspace isolation** — per-session GUID directory with ACL and deterministic cleanup
- **Batch processing** — queue, progress, retry, and cancellation across multiple files
- **Turkish PII focus** with extensible detector pipeline

## Screenshots

### Main Interface
<img width="959" height="515" alt="image" src="https://github.com/user-attachments/assets/532a1cde-293f-4563-9961-3199c27fa426" />

### PII Detection, Redaction and Verification
<img width="958" height="515" alt="image" src="https://github.com/user-attachments/assets/6ea2f453-893b-4213-bd4f-13acc6aeee56" />



## Supported Detection Types

| Category | Types |
|---|---|
| **Identity (Kimlik)** | `TcKimlikNo`, `TaxId`, `PassportNo`, `SgkNo` |
| **Contact (İletişim)** | `Phone`, `Email`, `Address`, `FullName`, `FirstName`, `LastName`, `MotherName`, `FatherName`, `Username` |
| **Financial (Finansal)** | `Iban`, `CreditCard`, `CardExpiry`, `Cvv` |
| **Network (Ağ)** | `IpAddress`, `Url`, `MacAddress` |
| **Utilities (Abonelik / Sayaç)** | `TesisatNo`, `AboneNo`, `SayacNo` |
| **Legal / Customer (Hukuki / Müşteri)** | `MusteriNo`, `DosyaNo`, `DavaNo`, `LicensePlate`, `Date`, `BloodType` |
| **Platform (Teknik / Platform)** | `Secret`, `PossiblePersonalData` (heuristic), structured XLSX field types |

| Category | Types |
|---|---|
| Identity | `TcKimlikNo`, `TaxId`, `PassportNo`, `SgkNo` |
| Contact | `Phone`, `Email`, `Address`, `FullName`, `FirstName`, `LastName`, `MotherName`, `FatherName`, `Username` |
| Financial | `Iban`, `CreditCard`, `CardExpiry`, `Cvv` |
| Network | `IpAddress`, `Url`, `MacAddress` |
| Utilities | `TesisatNo`, `AboneNo`, `SayacNo` |
| Legal / Customer | `MusteriNo`, `DosyaNo`, `DavaNo`, `LicensePlate`, `Date`, `BloodType` |
| Platform | `Secret`, `PossiblePersonalData` (heuristic), structured XLSX field types |

Detectors are independent components (TcKimlikDetector, PhoneDetector, EmailDetector, IbanDetector, PersonNameDetector, AddressDetector, InstallationNumberDetector / Tesisat, etc.) flowing through a common Detection model. Results include type, value, context, confidence, bounding box, and page number.

Secret detection supports technical patterns such as `FALCON_CLIENT_SECRET`; detected secret values are fully masked as `[SECRET]`.

## Supported Formats

- **PDF** — text ingestion and PII detection via PdfPig; redaction is currently unsupported and fails securely without creating an output.
- **DOCX** — DocumentFormat.OpenXml ingestion and redaction
- **XLSX** — DocumentFormat.OpenXml ingestion and redaction, including structured detectors
- **TXT** — plain text ingestion and redaction
- **UDF** — custom delimited format ingestion and redaction
- **Images** — PNG/JPEG etc. via ImageSharp, with optional local OCR pre-processing

All supported ingestion adapters produce the Common Document Model (`Document → DocumentPage → TextBlock → TextSpan`).

Flow: `Input → Document Adapter → Common Document Model → Detection Engine → Redaction/Planner → Renderer → Verification`

## Security Model

- **Local-only**: verified zero outbound network calls; no HTTP/DNS usage at runtime; Windows.Media.Ocr reflection only (OS-bundled, no network)
- **Temp workspace**: `%TEMP%\SafeCopy\{guid}` per session, ACL-restricted, cleanup on dispose, and stale-workspace recovery via `CleanupStaleWorkspaces`
- **Original immutability**: SHA256 hash captured on load and verified; original file never overwritten
- **Output verification**: `VerificationEngine` reloads output via `DocumentEngine` and re-runs detectors; residual PII → verification failure, no "Safe Copy Ready"
- **Manual validation**: documented offline test and Windows Firewall outbound block test.

## Installation (.NET 10)

**Prerequisites:** Windows 11, .NET 10 SDK, `net10.0-windows` with WPF.

```powershell
# Clone
git clone https://github.com/furkaniltas/SafeCopy.git SafeCopy
cd SafeCopy

# Restore & build
dotnet build -c Release
# or: MSBuild.exe SafeCopy.slnx /t:Build /p:Configuration=Release

# Run
dotnet run --project src/SafeCopy.App/SafeCopy.App.csproj -c Release
# Executable: SafeCopy.App.exe
# Application version: 1.0.0 (v1.0.0 release)
```

Publish example:
```powershell
dotnet publish src/SafeCopy.App/SafeCopy.App.csproj -c Release -o C:\Temp\SafeCopyRelease
```

Assets: `src/SafeCopy.App/Assets/Icons/SafeCopy.ico`

## Usage

1. Launch SafeCopy (Title: **SafeCopy**, Subtitle: *Hassas Veriler Güvende — Her Zaman Sizin Kontrolünüzde*).
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

Verification re-loads the generated output and re-runs the detection pipeline. Residual detectable PII causes verification to fail and blocks the safe-copy result.

## Privacy

- No cloud processing, external AI APIs, telemetry, analytics, remote configuration, CDN resources, or external OCR services.
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

Contributions are welcome.

Please keep the existing architecture intact, run the build and test suite before submitting changes, and use synthetic or anonymized test data only.

For security vulnerabilities, please follow `SECURITY.md` instead of opening a public issue or discussion.

## Security

See `SECURITY.md` for reporting, boundaries, and verification.

## License

MIT License — see `LICENSE`. Copyright (c) 2026 SafeCopy.

SafeCopy is released under the MIT License, permitting use, copy, modification, distribution, and commercial use under the terms in `LICENSE`.

