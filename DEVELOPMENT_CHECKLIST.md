# Eksim SafeCopy — Development Checklist

---

# Phase 0 — Product, Security and Architecture Decisions

- [x] Product name `Eksim SafeCopy` finalized
- [x] No legacy product name references found in repository
- [x] Product purpose documented
- [x] Windows 11 target confirmed
- [x] Fully local operation model confirmed
- [x] No external AI APIs decision
- [x] No Cloud OCR decision
- [x] No Telemetry decision
- [x] No Analytics decision
- [x] No Remote configuration decision
- [x] No Remote logging decision
- [x] Threat model created
- [x] Untrusted document input threat model created
- [x] ZIP/XML document attack surface evaluated
- [x] OCR attack surface evaluated
- [x] Temporary workspace security model created
- [x] Original document immutability guaranteed
- [x] Output verification mandatory acceptance criteria

---

# Phase 1 — Solution and Core Architecture

- [x] `.NET 10` solution
- [x] WPF application
- [x] Windows 11 target
- [x] `EksimSafeCopy.App`
- [x] `EksimSafeCopy.Core`
- [x] `EksimSafeCopy.DocumentEngine`
- [x] `EksimSafeCopy.Detectors`
- [x] `EksimSafeCopy.Ocr`
- [x] `EksimSafeCopy.Renderer`
- [x] `EksimSafeCopy.Infrastructure`
- [x] Test projects
- [x] Project references
- [x] Core layer WPF/file-system/Windows API independence
- [x] Dependency Injection
- [x] Interface/implementation separation
- [x] Configuration abstraction
- [x] Cancellation/timeout model
- [x] Error model
- [x] Basic test infrastructure
- [x] First clean build
- [x] First test run

---

# Phase 2 — Common Document Model

- [ ] `Document`
- [ ] `DocumentPage`
- [ ] `TextBlock`
- [ ] `TextSpan`
- [ ] `BoundingBox`
- [ ] `DocumentMetadata`
- [ ] `SourceReference`
- [ ] `Detection`
- [ ] `DetectionType`
- [ ] Confidence model
- [ ] Detection state
- [ ] Selection/masking state
- [ ] Native text/OCR text distinction
- [ ] Coordinate system
- [ ] Text span → bounding box relationship
- [ ] Cross-run/cross-block text support
- [ ] Unit tests
- [ ] Coordinate mapping tests

---

# Phase 3 — Document Ingestion

## PDF
- [ ] Native text PDF
- [ ] PDF metadata
- [ ] Page count
- [ ] Text coordinates
- [ ] Scanned PDF detection
- [ ] Embedded image detection
- [ ] Corrupt PDF handling
- [ ] PDF size limit
- [ ] Timeout/cancellation

## DOCX
- [ ] DOCX reading
- [ ] Paragraph extraction
- [ ] Run extraction
- [ ] Logical text reconstruction
- [ ] Tables
- [ ] Headers
- [ ] Footers
- [ ] Text box support strategy
- [ ] Document properties
- [ ] Malformed DOCX handling

## XLSX
- [ ] Workbook
- [ ] Worksheet
- [ ] Cell values
- [ ] Formula handling strategy
- [ ] Hidden sheets
- [ ] Hidden rows/columns
- [ ] Comments/notes
- [ ] Hyperlinks
- [ ] Workbook metadata

## TXT
- [ ] Encoding detection
- [ ] UTF-8
- [ ] UTF-16
- [ ] Turkish characters
- [ ] Large file handling

## UDF
- [ ] UDF format research
- [ ] Real UDF samples for testing
- [ ] Parser strategy
- [ ] Content extraction
- [ ] UDF security validation
- [ ] Fixture tests
- [ ] UDF → Document Model
- [ ] Supported output strategy

## Images
- [ ] PNG
- [ ] JPG/JPEG
- [ ] Metadata
- [ ] Resolution
- [ ] Orientation

---

# Phase 4 — Secure File Processing

- [ ] File size limit
- [ ] Extension + actual format validation
- [ ] File signature validation
- [ ] Path traversal protection
- [ ] Symlink/reparse point handling
- [ ] ZIP bomb protection
- [ ] Compression ratio limit
- [ ] XML entity protection
- [ ] Nested archive handling
- [ ] Extraction workspace isolation
- [ ] Temporary file lifecycle
- [ ] Cancellation cleanup
- [ ] Exception cleanup
- [ ] Stale temp cleanup
- [ ] Original file read-only processing
- [ ] Original overwrite prevention test

---

# Phase 5 — PII Detection Engine

Detection Engine will NOT only use regex.

Detector may use when needed:
- Pattern
- Checksum/validation
- Context
- Document structure
- Confidence
- Location

## Deterministic Detectors

- [ ] T.C. Kimlik No
- [ ] Telefon
- [ ] E-posta
- [ ] IBAN
- [ ] Vergi Kimlik No
- [ ] Pasaport No
- [ ] Tarih
- [ ] Plaka
- [ ] Tesisat No
- [ ] Abone No
- [ ] Sayaç No
- [ ] Müşteri No
- [ ] Dosya No
- [ ] Dava No

## Context Detection

- [ ] `T.C. Kimlik No`
- [ ] `TC Kimlik`
- [ ] `Kimlik Numarası`
- [ ] `Telefon`
- [ ] `GSM`
- [ ] `Cep`
- [ ] `Adres`
- [ ] `Tesisat No`
- [ ] `Abone No`
- [ ] `Sayaç No`
- [ ] `IBAN`
- [ ] Turkish context variations

## Name Detection

- [ ] Ad Soyad detection
- [ ] Turkish name dictionary
- [ ] Context scoring
- [ ] Corporate/company names false positive control
- [ ] Person name false-positive tests
- [ ] Multi-part names
- [ ] Turkish characters

## Address Detection

- [ ] İl
- [ ] İlçe
- [ ] Mahalle
- [ ] Sokak
- [ ] Cadde
- [ ] Bina no
- [ ] Daire no
- [ ] Address context
- [ ] Multi-line address

## Custom Detection

- [ ] Manual text addition
- [ ] Custom value addition
- [ ] Find all repetitions of same value
- [ ] Detection selection model

---

# Phase 6 — Confidence and Detection Review

- [ ] Confidence score
- [ ] High confidence
- [ ] Medium confidence
- [ ] Low confidence
- [ ] Detection reason
- [ ] Detection source
- [ ] Detection explanation
- [ ] False positive management
- [ ] False negative tests
- [ ] Detection grouping
- [ ] Repeated value grouping
- [ ] One-click select all repetitions

---

# Phase 7 — OCR Engine

OCR only activates when needed.

- [ ] `IOcrEngine`
- [ ] Local OCR implementation
- [ ] Turkish OCR
- [ ] Image preprocessing
- [ ] Deskew
- [ ] Denoise
- [ ] Contrast enhancement
- [ ] Thresholding
- [ ] Rotation detection
- [ ] Resolution normalization
- [ ] OCR confidence
- [ ] OCR bounding boxes
- [ ] Native/OCR distinction
- [ ] OCR timeout
- [ ] OCR cancellation
- [ ] OCR failure fallback
- [ ] Scanned PDF integration
- [ ] OCR test corpus
- [ ] Turkish scanned-document tests

---

# Phase 8 — Detection UI and Document Preview

- [ ] Main window
- [ ] Windows 11 layout
- [ ] Corporate header
- [ ] Eksim SafeCopy logo
- [ ] Drag & Drop
- [ ] File selection
- [ ] Supported format indicators
- [ ] Processing status
- [ ] Document preview
- [ ] Zoom
- [ ] Page navigation
- [ ] Detection highlighting
- [ ] Detection selection
- [ ] Detection type panel
- [ ] Confidence indicator
- [ ] Finding counters
- [ ] Selected/disabled state
- [ ] Right-side review panel
- [ ] Manual data addition
- [ ] "Mask all repetitions"
- [ ] "Remove mask"
- [ ] User warning dialogs
- [ ] Keyboard accessibility
- [ ] High DPI
- [ ] Windows 11 visual validation

---

# Phase 9 — Masking Engine

- [ ] Masking abstraction
- [ ] Full redaction
- [ ] Placeholder redaction
- [ ] Detection-type placeholder
- [ ] Partial masking
- [ ] User-selected masking
- [ ] Manual-value masking
- [ ] Repeated-value masking
- [ ] Original file immutability test

## PDF
- [ ] Native PDF text redaction
- [ ] Scanned PDF image redaction
- [ ] Visual redaction
- [ ] Underlying text removal
- [ ] Embedded text residual check
- [ ] Metadata sanitization
- [ ] PDF output validation

## DOCX
- [ ] Text replacement
- [ ] Cross-run replacement
- [ ] Tables
- [ ] Headers
- [ ] Footers
- [ ] Metadata sanitization
- [ ] Output validation

## XLSX
- [ ] Cell masking
- [ ] Formula/result handling
- [ ] Hidden sheet handling
- [ ] Hidden row/column handling
- [ ] Comments/notes
- [ ] Metadata
- [ ] Output validation

---

# Phase 10 — Output Verification

This phase is MANDATORY.

Masked output is re-scanned.

- [ ] Output scanner
- [ ] PII re-detection
- [ ] Remaining PII count
- [ ] Detection comparison
- [ ] Critical PII residual check
- [ ] Metadata residual check
- [ ] Embedded object check
- [ ] PDF text-layer residual check
- [ ] DOCX residual text check
- [ ] XLSX hidden-content residual check
- [ ] Verification failure → output NOT accepted as safe
- [ ] Verification success state

Acceptance criteria:
`Output with PII residue cannot be presented as safe copy.`

---

# Phase 11 — Privacy and Local-Only Security

- [ ] Network dependency audit
- [ ] HTTP/HTTPS call absence verified
- [ ] DNS dependency check
- [ ] Telemetry absence verified
- [ ] Analytics absence verified
- [ ] Cloud OCR absence verified
- [ ] External AI API absence verified
- [ ] Temp workspace security review
- [ ] Temp cleanup test
- [ ] Crash cleanup test
- [ ] Original file hash verification
- [ ] Output independence verification
- [ ] Windows Firewall outbound test
- [ ] Offline Windows 11 test

---

# Phase 12 — Batch Processing

Enterprise legal usage requires multiple documents processing.

- [ ] Multiple file selection
- [ ] Multiple drag & drop
- [ ] Processing queue
- [ ] Operation status
- [ ] Success/failed separation
- [ ] Per-file detection summary
- [ ] Per-file verification
- [ ] Cancel
- [ ] Retry
- [ ] Failed item isolation
- [ ] Original files preservation

---

# Phase 13 — User Experience and Corporate UI

Eksim IT Utility visual language referenced but NOT copied verbatim.

Eksim SafeCopy has its own product identity.

## Color Palette

### Primary Header

- Header background: `#0F2438`
- Primary green: `#2F8F4E`
- Header secondary text: `#B7C4D0`
- Header button border: `#33506A`
- Search background: `#16354D`
- Search border: `#33506A`
- Search placeholder: `#8FA3B4`

### Footer Group Strip

- Background: `#FAFBFC`
- Top border: `#DDE2E6`
- Text: `#64748B`

Group indicators:

- Eksim Enerji: `#2F8F4E`
- Dicle Grubu: `#D9622B`
- Gıda Grubu: `#C9A24B`
- Eksim Ventures: `#5B6EE8`

## Header

- [ ] Background `#0F2438`
- [ ] 34x34px brand block
- [ ] Brand block border-radius 8px
- [ ] Brand block background `#2F8F4E`
- [ ] White brand mark
- [ ] Brand mark font-weight 800
- [ ] Brand mark font-size 14px
- [ ] `Eksim SafeCopy` title
- [ ] Title white
- [ ] Title font-size approximately 19px
- [ ] Title font-weight 800
- [ ] Subtitle
- [ ] Subtitle color `#B7C4D0`
- [ ] Subtitle font-size approximately 12.5px
- [ ] Local-only/privacy status indicator
- [ ] Diagnostic/settings actions as required by final UX
- [ ] Search field uses `#16354D`
- [ ] Search border `#33506A`
- [ ] Search text white
- [ ] Search placeholder `#8FA3B4`

## Footer Group Signature Strip

Footer must be at the bottom of the application.

It is static/decorative and must not trigger filtering or navigation.

- [ ] Background `#FAFBFC`
- [ ] Top border `1px solid #DDE2E6`
- [ ] Horizontally centered
- [ ] Approximately 26px spacing between items
- [ ] Font size approximately 11.5px
- [ ] Text color `#64748B`
- [ ] 7x7px circular indicators

Items:

- [ ] Green dot `#2F8F4E` + `Eksim Enerji`
- [ ] Orange dot `#D9622B` + `Dicle Grubu`
- [ ] Gold/mustard dot `#C9A24B` + `Gıda Grubu`
- [ ] Purple-blue dot `#5B6EE8` + `Eksim Ventures`

Footer items are decorative only.

They are not buttons.

They do not trigger filtering.

They do not navigate anywhere.

## General UI

- [ ] Navigation
- [ ] Cards
- [ ] Typography
- [ ] Icons
- [ ] Empty states
- [ ] Loading states
- [ ] Success states
- [ ] Warning states
- [ ] Error states
- [ ] Confirmation dialogs
- [ ] Windows 11 DPI scaling
- [ ] Accessibility
- [ ] Keyboard navigation
- [ ] Tooltip/help
- [ ] Professional error messages

Visual polish happens after business/security functionality is stable.

---

# Phase 14 — Diagnostics

- [ ] Application version
- [ ] Windows version
- [ ] .NET runtime
- [ ] OCR engine status
- [ ] Required local component status
- [ ] Disk space
- [ ] Temp workspace status
- [ ] Local storage status
- [ ] Offline operation status
- [ ] Diagnostic report
- [ ] Diagnostic report export
- [ ] Diagnostic report contains no PII

---

# Phase 15 — Testing and Security Validation

## Unit Tests

- [ ] Core models
- [ ] Detection rules
- [ ] Confidence calculation
- [ ] Document Model
- [ ] Masking
- [ ] Verification
- [ ] Configuration
- [ ] Security helpers

## Integration Tests

- [ ] PDF ingestion
- [ ] DOCX ingestion
- [ ] XLSX ingestion
- [ ] TXT ingestion
- [ ] UDF ingestion
- [ ] OCR
- [ ] PDF masking
- [ ] DOCX masking
- [ ] XLSX masking
- [ ] Output verification

## Security Tests

- [ ] Malformed PDF
- [ ] Malformed DOCX
- [ ] Malformed XLSX
- [ ] Malformed UDF
- [ ] ZIP bomb
- [ ] Path traversal
- [ ] Oversized file
- [ ] Corrupt document
- [ ] OCR failure
- [ ] PII residual data
- [ ] Metadata leakage
- [ ] Hidden sheet leakage
- [ ] Hidden document content
- [ ] Temporary file leakage
- [ ] Network activity

## Detection Corpus

- [ ] TC Kimlik test corpus
- [ ] Turkish name corpus
- [ ] Turkish address corpus
- [ ] Telephone corpus
- [ ] IBAN corpus
- [ ] Energy-sector document corpus
- [ ] Legal document corpus
- [ ] OCR corpus
- [ ] False-positive corpus
- [ ] False-negative corpus

Test corpus will NOT contain real citizen/employee data.

---

# Phase 16 — Packaging and Windows 11

- [ ] EXE/MSIX/MSI packaging strategy
- [ ] Installation
- [ ] Uninstall
- [ ] Upgrade
- [ ] Per-user/per-machine decision
- [ ] Corporate deployment strategy
- [ ] Silent installation
- [ ] Intune compatibility
- [ ] GPO/SCCM compatibility
- [ ] Windows 11 clean VM
- [ ] Standard user
- [ ] Administrator
- [ ] Offline installation
- [ ] Code signing
- [ ] Installer integrity
- [ ] Antivirus/EDR compatibility
- [ ] CrowdStrike/SentinelOne/Trellix environment tests
- [ ] Clean uninstall

---

# Phase 17 — Release and Corporate Pilot

- [ ] Release checklist
- [ ] Versioning
- [ ] CHANGELOG
- [ ] README
- [ ] Installation Guide
- [ ] User Guide
- [ ] Security Architecture
- [ ] Threat Model
- [ ] Privacy Architecture
- [ ] Supported Formats
- [ ] Known Limitations
- [ ] Troubleshooting
- [ ] Pilot deployment package
- [ ] Legal unit pilot group
- [ ] User feedback
- [ ] False positive analysis
- [ ] False negative analysis
- [ ] OCR performance analysis
- [ ] Performance analysis
- [ ] Memory analysis
- [ ] Large-file testing
- [ ] Batch processing testing
- [ ] InfoSec review
- [ ] Pilot acceptance criteria
- [ ] Production release

---

# Phase 18 — Future Version / Post-MVP

Core security and document processing must be complete before these features.

- [ ] Central policy
- [ ] Central detection rule distribution
- [ ] Corporate custom PII dictionary
- [ ] Central configuration
- [ ] Admin console
- [ ] Microsoft Purview integration research
- [ ] DLP integration research
- [ ] AI gateway integration
- [ ] User-based policy
- [ ] Department-based policy
- [ ] Corporate masking profiles
- [ ] Multi-language support
- [ ] Advanced local NLP model
- [ ] Advanced OCR model
- [ ] Office/PDF plugin integration

---

# Release Acceptance Criteria

Release candidate is NOT complete until ALL below are done.

- [ ] Runs on Windows 11
- [ ] PDF processes
- [ ] DOCX processes
- [ ] XLSX processes
- [ ] TXT processes
- [ ] UDF supported or unsupported variants documented
- [ ] Scanned PDF processed via OCR
- [ ] TC Kimlik No detected
- [ ] Ad Soyad detected
- [ ] Address detected
- [ ] Telefon detected
- [ ] E-posta detected
- [ ] Doğum tarihi detected
- [ ] Tesisat/abone/sayaç numaraları detected
- [ ] IBAN detected
- [ ] User can toggle detections
- [ ] User can add manual values
- [ ] All repetitions of single value selectable
- [ ] Original file unchanged
- [ ] Masked PDF producible
- [ ] Masked DOCX producible
- [ ] Masked XLSX producible
- [ ] Output re-scanned
- [ ] Output with PII residue NOT presented as safe
- [ ] App requires NO internet during normal operation
- [ ] No Telemetry
- [ ] No Cloud API
- [ ] Temp files cleaned
- [ ] Security tests pass
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Windows 11 clean VM test passes
- [ ] Installer signed
- [ ] Corporate pilot successful

---

# Development Rules

1. Phases are sequential.
2. No next phase until current complete.
3. `[x]` only with code + test + validation proof.
4. File/class creation alone is NOT completion proof.
5. Failed tests NOT hidden.
6. Tests NOT skipped.
7. Security controls NOT disabled for test convenience.
8. Agent does NOT add new product requirements.
9. No unjustified dependencies.
10. Every dependency evaluated for license/security/maintenance/offline.
11. No external services added.
12. No real citizen/employee data used.
13. Test data synthetic or anonymized.
14. Real user documents NOT committed to repository.
15. UI does NOT precede business/security architecture.
16. Temporary workaround NOT accepted as permanent solution.
17. Build/test after every meaningful change.
18. Checklist updated at phase end.
19. Phase NOT marked `[x]` until complete.
20. Git commit created at phase completion.

---

# Git Rules

Every session starts with:

```powershell
git status
```

Do NOT delete existing user changes.

Unexpected uncommitted changes? Analyze state first.

Checkpoint commit before code changes if needed.

After build/test:

```powershell
git status
```

Check staged files.

NEVER commit:

```
bin/
obj/
.vs/
TestResults/
temp/
temporary workspaces
user documents
real PII data
generated local secrets
```

Descriptive commit per completed phase.

Example:

```
phase 0: establish product and security boundaries
phase 1: create solution and core architecture
phase 2: add common document model
phase 3: implement document ingestion
phase 4: harden document processing
phase 5: implement pii detection engine
```

---

# Build and Test Standard

Build with Visual Studio MSBuild.

Preferred:

```powershell
MSBuild.exe EksimSafeCopy.slnx /t:Build /p:Configuration=Debug
```

Test with:

```powershell
vstest.console.exe <test-dll> /Logger:Console
```

`dotnet build` NOT mandatory; prefer VS MSBuild if available.

Every phase end:

1. Build
2. Unit tests
3. Relevant integration tests
4. Relevant security tests
5. Run app if UI changes
6. Verify actual behavior

Report test results numerically.

Example:

```
Build: 0 Error, 0 Warning
Tests: 84/84 Passed
Security tests: 22/22 Passed
```

Failed test = phase NOT complete.

---

# Real File and Personal Data Rule

NO real legal documents, citizen documents, or employee documents in repository.

Create synthetic examples for test.

Example:

```
Ahmet Yılmaz
11111111111
0532 000 00 00
Örnek Mahallesi Örnek Sokak No:10
TR00 0000 0000 0000 0000 0000 00
```

Real corporate documents ONLY for manual user testing; NOT copied to repository or sent externally.

---

# Document Model Rule

PDF, DOCX, XLSX, TXT, UDF, image ingestion implementations do NOT connect directly to UI.

All MUST use:

```
Input
 ↓
Document Adapter
 ↓
Common Document Model
 ↓
Detection Engine
```

UI must NOT know format internals.

---

# Detection Engine Rule

Detectors NOT implemented inside UI.

Example:

```
TcKimlikDetector
PhoneDetector
EmailDetector
IbanDetector
NameDetector
AddressDetector
TesisatDetector
```

Must be independent components.

Detection result flows to UI via common `Detection` model.

---

# Output Security Rule

After masked file created:

```
Output
 ↓
Re-scan
 ↓
PII detection
 ↓
Residual PII?
```

Check MUST run before user sees:

> "Safe copy ready"

If residual PII:

```
Security verification failed
```

---

# UI Design Rule

Eksim IT Utility current corporate visual language MAY be referenced.

But NO verbatim copy.

Eksim SafeCopy MUST have own product identity.

Main visual character:

- Corporate
- Clean
- Security-focused
- Windows 11 native feel
- No unnecessary animation
- White/light workspace
- Dark navy header
- Eksim green primary action
- Clear detection state
- Clear security status

---

# Phase Completion Protocol

Every phase completion follows this order:

```
1. Complete code changes
2. Build
3. Test
4. Security validation
5. UI validation if needed - run app
6. Update checklist
7. Git status
8. Stage expected files
9. Create commit
10. Post-commit git status
11. Proceed to next phase
```

If phase result fails, do NOT proceed to next phase.

---

# Agent Decision Principle

Technical choice with multiple options:

1. First inspect existing repository.
2. Check existing dependencies.
3. Research current official documentation.
4. Evaluate security impact.
5. Evaluate offline operation requirement.
6. Evaluate license.
7. Choose simplest sustainable solution.
8. Document decision in relevant technical doc.

Do NOT expand product scope independently.

---

# IMPORTANT: Phase Gate

For a phase:

```
Implementation complete
```

is NOT sufficient.

ALL below MUST be satisfied:

```
Code
+
Build
+
Tests
+
Validation
+
Checklist
+
Git commit
```

If any missing, phase is NOT `[x]`.

---

# FIRST OPERATION TO RUN NOW

Immediately:

```powershell
git status
```

Then inspect repository.

Preserve existing user changes.

Create/update `AGENTS.md` and `DEVELOPMENT_CHECKLIST.md`.

Verify product name is `Eksim SafeCopy` everywhere in repository.

Then apply Phase 0.

Complete Phase 0 with build/test/validation.

Create commit.

Then proceed to Phase 1.

Continue through **Phase 0 → Phase 18** sequentially, no phase skipped, no incomplete phase marked `[x]`.

Do NOT ask user permission per phase.

BUT if genuine conflict between security requirement and another requirement, do NOT guess; explicitly state problem and protect security boundary.

---

# FINAL RULE

Priority order in this project:

```
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
```

Visually beautiful but incomplete masking app is NOT successful.

Technically working but can modify original document app is NOT successful.

App that can leave PII residue in masked output is NOT successful.

These criteria do NOT change during development.