# Changelog

All notable changes to SafeCopy.

## [Unreleased]

### Changed
- Project identity standardized as SafeCopy.
  - Solution: `SafeCopy.slnx`
  - Projects: `src/SafeCopy.*` (App, Core, DocumentEngine, Detectors, Ocr, Renderer, Infrastructure) and `tests/SafeCopy.*.Tests` (including Benchmark)
  - Namespaces: `SafeCopy.*` (`using`/`namespace`/`clr-namespace`/`x:Class`)
  - Icon: `Assets/Icons/SafeCopy.ico`, manifest `SafeCopy.App`, assembly `SafeCopy` 1.0.0.0
  - UI: Title `SafeCopy`, subtitle `Secure PII Redaction — Local First`, footer `SafeCopy — Local-first PII redaction`, logo `SC`
  - Temp workspace: `%TEMP%\SafeCopy\{guid}`
- Documentation refreshed: `AGENTS.md`, `DEVELOPMENT_CHECKLIST.md`, `docs/security/*.md`, new `README.md`, `SECURITY.md`, `CONTRIBUTING.md`.

### Security
- Local-only boundaries, output verification, and temp isolation verified and documented (732 tests passing).

## [1.0.0] - 2026-09-23

- Hardened environment secret detection; 732/732 tests passing; build 0/0 (baseline 831435b).
- Full feature set: Common Document Model, independent detectors (TC Kimlik, Phone, Email, IBAN, Tesisat etc.), local OCR (TR/EN), format-specific redaction (DOCX/XLSX/TXT/UDF/Images, PDF fails securely), batch processing, verification engine, WPF MVVM UI.

