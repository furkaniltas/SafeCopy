# Eksim SafeCopy — Agent Instructions

## Project Context

**Product Name:** Eksim SafeCopy  
**Platform:** Windows 11  
**Framework:** .NET 10, WPF, MVVM, Clean Architecture  
**Core Principle:** Fully local operation — no internet, no cloud APIs, no telemetry

## Architecture Rules

### Solution Structure
```
EksimSafeCopy.slnx
├── src
│   ├── EksimSafeCopy.App
│   ├── EksimSafeCopy.Core
│   ├── EksimSafeCopy.DocumentEngine
│   ├── EksimSafeCopy.Detectors
│   ├── EksimSafeCopy.Ocr
│   ├── EksimSafeCopy.Renderer
│   └── EksimSafeCopy.Infrastructure
├── tests
│   ├── EksimSafeCopy.Core.Tests
│   ├── EksimSafeCopy.DocumentEngine.Tests
│   ├── EksimSafeCopy.Detectors.Tests
│   ├── EksimSafeCopy.Ocr.Tests
│   ├── EksimSafeCopy.Renderer.Tests
│   └── EksimSafeCopy.Security.Tests
├── docs
└── installer
```

### Layer Responsibilities
- **Core:** Domain models, interfaces, business logic — NO WPF/file-system/Windows API dependencies
- **DocumentEngine:** Document ingestion (PDF, DOCX, XLSX, TXT, UDF, Images) → Common Document Model
- **Detectors:** PII detection implementations (TC Kimlik, Phone, Email, IBAN, Name, Address, etc.)
- **Ocr:** Local OCR engine for scanned documents
- **Renderer:** Masking/redaction output for each format
- **Infrastructure:** Cross-cutting concerns (configuration, DI, logging abstraction)
- **App:** WPF UI, composition root

### Document Model Rule
All ingestion adapters MUST produce the Common Document Model:
```
Input → Document Adapter → Common Document Model → Detection Engine
```
UI must NOT know about format internals.

### Detection Engine Rule
Detectors are independent components:
```
TcKimlikDetector, PhoneDetector, EmailDetector, IbanDetector, NameDetector, AddressDetector, TesisatDetector
```
Results flow through common `Detection` model to UI.

### OCR Rule
OCR only activates when needed (scanned PDF, images). Local-only. Turkish language support.

### Masking Rule
- Original file NEVER modified
- Output re-scanned for residual PII before delivery
- If residual PII found → Security verification failed

### Output Verification Rule (MANDATORY)
```
Output → Re-scan → PII detection → Residual PII?
```
No "Safe Copy Ready" message without verification pass.

## Local-Only Security Boundary

**FORBIDDEN:**
- Cloud OCR
- ChatGPT/Claude/Gemini/Copilot APIs
- Telemetry/Analytics
- Remote configuration/logging
- CDN/Remote fonts
- Any outbound network calls during normal operation

**REQUIRED:**
- Offline operation verified
- Temp workspace isolation + cleanup
- Original file hash verification
- Windows Firewall outbound test pass

## Git Rules
- `git status` at session start
- Never commit: bin/, obj/, .vs/, TestResults/, temp/, user documents, real PII
- Descriptive commits per phase: `phase N: description`
- Phase gate: Code + Build + Tests + Validation + Checklist + Commit

## Build/Test Standard
```powershell
MSBuild.exe EksimSafeCopy.slnx /t:Build /p:Configuration=Debug
vstest.console.exe <test-dll> /Logger:Console
```
Report: `Build: 0 Error, 0 Warning | Tests: 84/84 Passed | Security tests: 22/22 Passed`

## Test Data Rules
- Synthetic/anonymized only
- No real citizen/employee data
- Example: `Ahmet Yılmaz`, `11111111111`, `0532 000 00 00`, `TR00 0000 0000 0000 0000 0000 00`

## Phase Management
Phases are sequential. No phase skip. `[x]` only with: Code + Build + Tests + Validation + Checklist + Commit.

Priority Order:
1. Security
2. Data Privacy
3. Correct Detection
4. Correct Irreversible Masking
5. Output Verification
6. Format Support
7. Testability
8. Performance
9. UX
10. Visual Polish