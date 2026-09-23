# Contributing to SafeCopy

## Getting Started

- Windows 11, .NET 10 SDK, `net10.0-windows` with WPF.
- Clone, then build and test:

```powershell
dotnet build -c Release
dotnet test -c Release
```

Both must be `0 Error, 0 Warning` and `732/732` tests passing before any PR.

## Architecture

- Keep **Clean Architecture** layering:
  - `SafeCopy.Core` — domain models, interfaces, business logic; **no** WPF/file-system/Windows API
  - `SafeCopy.DocumentEngine` — ingestion adapters → Common Document Model (`Input → Adapter → Common Model → Detection Engine`)
  - `SafeCopy.Detectors` — independent detectors flowing through `Detection`
  - `SafeCopy.Ocr` — local OCR only when needed (TR/EN), no cloud
  - `SafeCopy.Renderer` — masking/redaction per format, never modifies original, re-scanned for residual PII
  - `SafeCopy.Infrastructure` — configuration, DI, logging abstraction, temp workspace
  - `SafeCopy.App` — WPF UI (MVVM, composition root)

- Do not change detection/redaction/verification logic without explicit scope.
- Keep `GenerateAssemblyInfo false`, `UseWPF true`, `net10.0-windows`, `TreatWarningsAsErrors`.

## Code Style & Security

- No outbound network, no cloud OCR, no AI APIs, no telemetry.
- Synthetic/anonymized test data only (e.g., `Ahmet Yılmaz`, `11111111111`). Never commit real PII, `bin/`, `obj/`, `.vs/`, `TestResults/`, `temp/`.
- Temp workspace under `%TEMP%\SafeCopy\{guid}` with deterministic cleanup.
- All outputs must pass verification before “Safe Copy Ready”.

## Testing

- Do not delete or disable tests. Keep 732 tests passing.
- Keep `SecretDetector` patterns (e.g., `FALCON_CLIENT_SECRET`) unchanged — they are technical, not branding.
- Use `C:\Temp\SafeCopyRelease` for publish tests; do not pollute the repo.

## Pull Requests

- Use `git mv` for renames to preserve history; ensure `git status` shows `R` not `D+A`.
- Reference phases/issues clearly; include build/test output.
- Update `AGENTS.md`, `DEVELOPMENT_CHECKLIST.md`, and `docs/security/` as needed without inventing license or company claims.

## Rebranding Notes

- Namespace root is `SafeCopy` (not `EksimSafeCopy`).
- Solution `SafeCopy.slnx`, projects `src/SafeCopy.*` and `tests/SafeCopy.*.Tests`.
- Executable `SafeCopy.exe`, icon `Assets/Icons/SafeCopy.ico`, subtitle `Secure PII Redaction — Local First`.
- Footer is `SafeCopy — Local-first PII redaction`; no company divisions.

