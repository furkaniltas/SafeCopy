# Security Policy

## Supported Versions

| Version | Supported |
|---|---|
| 1.0.x | ✓ |

## Reporting a Vulnerability

Report security issues privately. Do not open a public issue for vulnerabilities.

- Provide affected version, reproduction steps, and impact.
- Expect an initial response within 7 days and a fix or mitigation plan as appropriate.
- Keep details confidential until a fix is released.

Contacts and disclosure process should be documented by the maintainers (no external reporting channel is configured in this repository at present).

## Security Boundaries (Local-Only)

SafeCopy is fully offline:

- **Forbidden:** cloud OCR, external AI APIs, telemetry/analytics, remote config/logging, CDN/remote fonts, any outbound network calls during normal operation.
- **Required:** temp workspace isolation + cleanup (`%TEMP%\SafeCopy\{guid}` with ACL), original file hash verification, output verification re-scan, Windows Firewall outbound test pass, offline operation verified.

Implementation references:
- `src/SafeCopy.Infrastructure/FileSystem.cs` — `SecureTempWorkspace`
- `src/SafeCopy.DocumentEngine/Security/DocumentSecurityValidator.cs`
- `src/SafeCopy.Ocr/LocalOcrEngine.cs`
- `src/SafeCopy.Renderer/Verification/VerificationEngine.cs`

## Verification

Every output is reloaded and re-scanned by the detection engine. If residual PII remains (especially critical types like `TcKimlikNo`, `Iban`), verification fails and the safe copy is not marked ready.

## Hardening Notes

- Temp workspace is per-session GUID, ACL-restricted, disposed/finalized, with stale-workspace reaper.
- Original file is never overwritten; hash is captured on load.
- No network usage is expected; verify with Windows Firewall block and offline tests.

