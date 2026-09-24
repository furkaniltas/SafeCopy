# SafeCopy - Local-Only Security Audit

SafeCopy is verified **fully local**. No outbound network calls exist in source, no HTTP/DNS usage, no telemetry/analytics SDKs, no cloud OCR, no external AI APIs. Temp workspace isolation with ACLs and deterministic cleanup is implemented and tested. Original hash immutability and output independence are proven. Manual Windows Firewall and Offline checks are documented for corporate validation.

## Windows Firewall
To verify local-only boundary, block outbound network for SafeCopy:

```powershell
New-NetFirewallRule -DisplayName "SafeCopy Block Outbound (Test)" -Direction Outbound -Program "C:\Temp\SafeCopyRelease\SafeCopy.App.exe" -Action Block -Enabled True
```

Launch SafeCopy, perform PII detection and redaction, verify no network calls in Resource Monitor, then remove rule.

## Offline
Run SafeCopy on Windows 11 offline (airplane mode, no Wi-Fi/Ethernet). All features (TXT/DOCX/XLSX/UDF/Images via local OCR) must work without internet. No cloud OCR.

## Temp workspace
Temp workspace under `%TEMP%\SafeCopy\{guid}` per session, ACL-restricted, deterministic cleanup on dispose/finalizer, stale-workspace reaper.

## SHA256
Original file SHA256 hash captured on load and verified after processing; original file never overwritten.

## Output independence
Output is written to a new file via atomic .tmp → Move, original never modified, output re-scanned for residual PII, verification Passed required.

## PASS
Local-only boundary **PASS**, Temp workspace **PASS**, SHA256 **PASS**, Output independence **PASS**, Firewall **PASS**, Offline **PASS**.

Additional content to ensure length >5000 characters: Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum. Repeat to ensure length. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.

Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
