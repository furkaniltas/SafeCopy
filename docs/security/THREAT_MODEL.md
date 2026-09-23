# SafeCopy — Threat Model

## System Overview

SafeCopy is a Windows 11 desktop application that processes documents locally to detect and mask PII (Personally Identifiable Information) before users share documents with external AI services.

**Trust Boundary:** User's local machine only. No network communication during normal operation.

---

## Assets

| Asset | Classification | Description |
|-------|----------------|-------------|
| Original Documents | CONFIDENTIAL | User-provided PDF, DOCX, XLSX, TXT, UDF, Images |
| Extracted Text Content | CONFIDENTIAL | Text extracted from documents during processing |
| PII Detections | CONFIDENTIAL | Detected TC Kimlik, names, addresses, phones, emails, IBANs, etc. |
| Masked Output Documents | INTERNAL | Documents with PII redacted/masked |
| Temporary Workspace Files | CONFIDENTIAL | Intermediate files during processing |
| Configuration | INTERNAL | User preferences, detection settings |
| Audit Log (if any) | CONFIDENTIAL | Local-only processing history |

---

## Threat Actors

| Actor | Motivation | Capability |
|-------|------------|------------|
| Malicious Document Creator | Data exfiltration, code execution | Crafted malicious documents |
| Local Malware | Data theft, persistence | File system access, memory scraping |
| Insider (User) | Accidental leakage | Normal app usage |
| Supply Chain | Backdoor injection | Compromised dependencies |

---

## Attack Surfaces

### 1. Document Ingestion (Primary Attack Surface)

**Threat:** Malformed/crafted documents exploiting parser vulnerabilities

| Threat | Mitigation |
|--------|------------|
| PDF: JavaScript execution, embedded files, launch actions | Disable JS, ignore embedded files, no launch actions |
| PDF: ZIP bomb (embedded streams) | Stream size limits, decompression ratio limits |
| DOCX: XML External Entity (XXE) | Disable DTD processing, secure XML readers |
| DOCX: OOXML package traversal | Validate zip entries, prevent path traversal |
| XLSX: Formula injection | Treat formulas as text, no evaluation |
| XLSX: Hidden sheets/rows/columns data leakage | Process all sheets, rows, columns |
| UDF: Format parsing exploits | Strict parser, size limits, validation |
| Images: Steganography, exploit payloads | No image execution, metadata stripping |

### 2. OCR Processing

**Threat:** Malicious images targeting OCR engine

| Threat | Mitigation |
|--------|------------|
| Image decompression bombs | Size/resolution limits before processing |
| Crafted images causing OCR engine crashes | Sandboxed/isolated OCR process, timeouts |
| Adversarial images causing misclassification | Confidence thresholds, human review |

### 3. Temporary Workspace

**Threat:** Data leakage through temp files

| Threat | Mitigation |
|--------|------------|
| Temp files readable by other users/processes | Per-session isolated directory, ACLs |
| Temp files not cleaned on crash | Try/finally, startup cleanup of stale dirs |
| Temp files on unencrypted disk | Document requirement for encrypted volumes |
| Symlink/reparse point attacks | Resolve paths, validate within workspace |

### 4. Output Generation

**Threat:** Incomplete masking, residual PII

| Threat | Mitigation |
|--------|------------|
| Text layer not fully redacted in PDF | Remove text, add visual redaction, verify |
| Metadata retention (author, custom props) | Strip all metadata on output |
| Hidden content (DOCX text boxes, XLSX hidden sheets) | Process all document parts |
| Embedded objects retaining PII | Recursively process embedded objects |

### 5. Local-Only Security Boundary

**Threat:** Accidental or malicious network calls

| Threat | Mitigation |
|--------|------------|
| Telemetry/analytics libraries phoning home | No telemetry libraries, code audit |
| Auto-update checks | Disabled by default, manual only |
| Font/CDN loading | Embedded fonts only |
| OCR model downloads | Models bundled, no runtime downloads |
| Diagnostic reporting | Opt-in, explicit user action, no PII |

---

## STRIDE Analysis

### Spoofing
- **Malicious document pretending to be benign** → Format validation, signature verification
- **Output document claiming to be verified** → Verification result embedded in output metadata

### Tampering
- **Original document modified during processing** → Read-only file access, hash verification
- **Detection results modified** → In-memory processing, no intermediate persistence
- **Masked output tampered post-verification** → Verification runs immediately before delivery

### Repudiation
- **User denies document was processed** → Local audit log (optional, user-controlled)
- **App claims verification passed when it didn't** → Verification result cryptographically signed (local)

### Information Disclosure
- **PII in memory dumped** → Minimize retention, secure string handling, zeroize buffers
- **PII in temp files** → Encrypted temp, immediate cleanup
- **PII in crash dumps** → Disable crash reporting, no PII in logs
- **PII in swap/pagefile** → Consider `SetProcessWorkingSetSize`, secure memory

### Denial of Service
- **Large document consumes memory/CPU** → Size limits, streaming processing, timeouts
- **Malformed document hangs parser** → Parser timeouts, cancellation tokens
- **ZIP bomb exhausts disk** → Compression ratio limits, streaming extraction

### Elevation of Privilege
- **Document exploits parser to execute code** → No code execution in parsers, sandboxed OCR
- **Symlink attack writes outside workspace** → Path canonicalization, workspace confinement

---

## Security Requirements (Derived)

### SR-01: Document Processing Isolation
All document parsing occurs in-process with no code execution capabilities. No scripts, macros, or active content executed.

### SR-02: Input Validation
Every document validated for format compliance before deep processing. Size limits enforced at ingestion.

### SR-03: Temporary Workspace Security
- Unique per-session directory under `%TEMP%\SafeCopy\{guid}`
- Directory ACL: Current user only
- Automatic cleanup on normal exit, crash, and startup
- No sensitive data written unencrypted

### SR-04: Original Document Protection
- Opened read-only (`FileShare.Read`)
- SHA-256 hash computed before processing
- Hash verified after processing (original unchanged)
- Never written to, never deleted

### SR-05: Output Verification
- Every masked output re-scanned with full detection engine
- Zero residual PII tolerance for critical types (TC Kimlik, IBAN, etc.)
- Verification failure = output rejected, user notified

### SR-06: Network Isolation
- No outbound connections during normal operation
- Windows Firewall rule verification at startup (optional diagnostic)
- No telemetry, analytics, auto-update, font loading, model downloads

### SR-07: Memory Safety
- PII minimized in memory lifetime
- `SecureString` or equivalent for sensitive buffers where feasible
- No PII in exception messages, logs, or diagnostic output

### SR-08: Supply Chain Security
- Dependencies: Minimal, well-maintained, permissive licenses
- NuGet packages: Signed where available, verified hashes
- No dynamic code loading from untrusted sources

---

## Data Flow Diagram (Text)

```
User
  │
  ▼
[File Selection] ──▶ [Format Validation] ──▶ [Hash Original]
  │                                               │
  ▼                                               │
[Document Adapter] ──▶ [Common Document Model]    │
  │                      │                        │
  ▼                      ▼                        │
[Detection Engine] ◀────┘                        │
  │                                               │
  ▼                                               │
[Review UI] ◀──── User Selection                 │
  │                                               │
  ▼                                               │
[Masking Engine] ──▶ [Masked Output]              │
  │                      │                        │
  ▼                      ▼                        │
[Verification Scan] ◀───┘                        │
  │                      │                        │
  ▼                      ▼                        │
[PASS] ──▶ [Deliver Safe Copy]                   │
  │                                               │
  ▼                                               │
[Cleanup Temp] ──▶ [Verify Original Hash]        │
  │                                               │
  ▼                                               ▼
[Complete] ◀──────────────────────────────────────┘
```

---

## Residual Risks (Accepted)

| Risk | Likelihood | Impact | Acceptance Rationale |
|------|------------|--------|---------------------|
| Zero-day in .NET PDF/DOCX libraries | Low | High | Mitigated by input validation, size limits, no code execution |
| OCR engine vulnerability | Low | Medium | Local-only, sandboxed, timeout |
| Memory scraping by local malware | Medium | High | Accepted: malware on machine = full compromise anyway |
| Side-channel attacks (timing, cache) | Very Low | High | Not in threat model for desktop app |

---

## Verification Checklist (Phase Gate)

- [ ] Threat model documented
- [ ] Attack surfaces enumerated
- [ ] STRIDE analysis complete
- [ ] Security requirements derived
- [ ] Data flow diagram created
- [ ] Residual risks acknowledged
- [ ] All mitigations mapped to implementation phases

---

*Document Version: 1.0*  
*Phase: 0*  
*Classification: INTERNAL*