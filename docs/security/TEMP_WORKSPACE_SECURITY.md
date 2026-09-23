# SafeCopy — Temporary Workspace Security Model

## Overview

All document processing occurs in an isolated temporary workspace. This document defines the security model for workspace creation, usage, and cleanup.

---

## Workspace Structure

```
%TEMP%\SafeCopy\
├── {session-guid-1}\
│   ├── input\           # Copied/linked original documents (read-only)
│   ├── extracted\       # ZIP/XML extraction, image extraction
│   ├── ocr\             # Preprocessed images for OCR
│   ├── output\          # Masked documents before verification
│   └── verification\    # Re-scanned output for verification
├── {session-guid-2}\
└── ...
```

---

## Security Requirements

### SR-TEMP-01: Isolation
Each processing session gets a unique, unpredictable directory name (GUID).
- No cross-session contamination
- No predictable paths for attackers

### SR-TEMP-02: Access Control
Directory ACL: **Current user only** (no Administrators, SYSTEM, or other users).
- `icacls <dir> /inheritance:r /grant:r %USERNAME%:(OI)(CI)F`

### SR-TEMP-03: Encryption at Rest (Defense in Depth)
- Workspace created on encrypted volume (BitLocker) — **documented requirement**
- Application does not implement own encryption (OS responsibility)
- If volume not encrypted: logged in diagnostics, user warned

### SR-TEMP-04: No Persistent Sensitive Data
- All files deleted on normal completion
- All files deleted on exception/crash (best effort)
- Stale workspaces cleaned on application startup

---

## Lifecycle Management

### Creation (Session Start)

```csharp
public sealed class SecureTempWorkspace : IDisposable
{
    private readonly string _rootPath;
    private readonly string _sessionId;
    
    public SecureTempWorkspace()
    {
        _sessionId = Guid.NewGuid().ToString("N");
        _rootPath = Path.Combine(
            Path.GetTempPath(), 
            "SafeCopy", 
            _sessionId);
        
        // Create with secure ACL
        Directory.CreateDirectory(_rootPath);
        SetSecureAcl(_rootPath);
        
        // Create subdirectories
        foreach (var sub in new[] { "input", "extracted", "ocr", "output", "verification" })
        {
            Directory.CreateDirectory(Path.Combine(_rootPath, sub));
            SetSecureAcl(Path.Combine(_rootPath, sub));
        }
    }
    
    private void SetSecureAcl(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        var security = dirInfo.GetAccessControl();
        
        // Disable inheritance
        security.SetAccessRuleProtection(true, false);
        
        // Current user full control only
        var user = WindowsIdentity.GetCurrent().User;
        var rule = new FileSystemAccessRule(
            user, 
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
        
        security.SetAccessRule(rule);
        dirInfo.SetAccessControl(security);
    }
}
```

### Usage During Processing

| Subdirectory | Purpose | Retention |
|--------------|---------|-----------|
| `input/` | Original file copies (or hard links) | Until session end |
| `extracted/` | ZIP contents, XML parts, embedded images | Until session end |
| `ocr/` | Preprocessed images for OCR | Until OCR complete |
| `output/` | Masked documents before verification | Until verification |
| `verification/` | Re-scanned output for verification | Until verification complete |

**Rules:**
- Never write original documents to workspace (use read-only access or hard links)
- All intermediate files treated as confidential
- No logging of file contents to workspace
- Memory-first processing; disk only when necessary

### Cleanup (Session End)

```csharp
public void Dispose()
{
    Cleanup();
    GC.SuppressFinalize(this);
}

~SecureTempWorkspace()
{
    Cleanup();
}

private void Cleanup()
{
    try
    {
        if (Directory.Exists(_rootPath))
        {
            // Delete files first (bypass read-only)
            foreach (var file in Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories))
            {
                try 
                { 
                    File.SetAttributes(file, FileAttributes.Normal); 
                    File.Delete(file); 
                }
                catch { /* Log, continue */ }
            }
            
            // Delete directories bottom-up
            foreach (var dir in Directory.GetDirectories(_rootPath, "*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(dir, true); } catch { /* Log, continue */ }
            }
            
            try { Directory.Delete(_rootPath, true); } catch { /* Log */ }
        }
    }
    catch (Exception ex)
    {
        // Log cleanup failure — do not throw
        Logger.LogWarning($"Workspace cleanup failed: {ex.Message}");
    }
}
```

### Stale Workspace Cleanup (Application Startup)

```csharp
public static void CleanupStaleWorkspaces(TimeSpan maxAge)
{
    var root = Path.Combine(Path.GetTempPath(), "SafeCopy");
    if (!Directory.Exists(root)) return;
    
    var cutoff = DateTime.UtcNow - maxAge;
    
    foreach (var dir in Directory.GetDirectories(root))
    {
        try
        {
            var creationTime = Directory.GetCreationTimeUtc(dir);
            if (creationTime < cutoff)
            {
                // Best effort cleanup
                DeleteDirectoryRecursive(dir);
            }
        }
        catch { /* Ignore — may be in use by another session */ }
    }
}

// Called at app startup with maxAge = 24 hours
```

---

## Crash/Exception Handling

### Scenario: Application crashes during processing

**Guarantee:** Workspace will be cleaned up on **next application startup** (stale cleanup).

**Not Guaranteed:** Immediate cleanup (process terminated by OS).

**Mitigation:**
- Stale cleanup runs at startup (24-hour default)
- No sensitive data persists beyond 24 hours
- User can manually delete `%TEMP%\SafeCopy` anytime

### Scenario: Unhandled exception in processing

**Guarantee:** `Dispose()` called via `try/finally` or `using` pattern.

```csharp
using (var workspace = new SecureTempWorkspace())
{
    // All processing here
    // If exception thrown, Dispose() runs automatically
}
```

### Scenario: Power loss / OS crash

**Guarantee:** Stale cleanup on next boot + app start.

---

## Original Document Handling

### Principle: NEVER COPY ORIGINAL TO TEMP WORKSPACE

| Approach | Security | Performance | Implementation |
|----------|----------|-------------|----------------|
| **Read-only FileStream** | ✅ Best | ✅ Good | `File.OpenRead(path)` |
| **Hard Link** | ✅ Good | ✅ Best | `CreateHardLink()` — only if same volume |
| **Copy to workspace** | ❌ Risk | ❌ Slow | **FORBIDDEN** |

**Implementation:**
```csharp
// Preferred: Direct read-only access
using var fs = File.Open(originalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
// Process stream directly — no temp copy

// If library requires file path (some PDF libs):
// Use hard link if same volume, else read-only copy with immediate cleanup
```

---

## Verification Output Handling

Masked output written to `output/`, then re-scanned from `verification/`.

```csharp
// 1. Write masked output to workspace/output/
var maskedPath = Path.Combine(workspace.OutputPath, "masked_" + fileName);
maskingEngine.Write(maskedDocument, maskedPath);

// 2. Copy to verification (or re-read from output)
var verifyPath = Path.Combine(workspace.VerificationPath, "verify_" + fileName);
File.Copy(maskedPath, verifyPath);

// 3. Run verification scan on verifyPath
var verificationResult = verificationEngine.Scan(verifyPath);

// 4. If PASS: Copy to user's chosen output location
// 5. If FAIL: Delete both, report failure, NEVER deliver to user
```

---

## Disk Space Protection

| Limit | Value | Action on Exceed |
|-------|-------|------------------|
| Per-session max | 2 GB | Abort processing, cleanup, error |
| Per-file max (input) | 500 MB | Reject at ingestion |
| Total temp usage | 10 GB | Warn user, pause new sessions |

**Implementation:** Check `DriveInfo.AvailableFreeSpace` before large operations.

---

## Test Cases (Security Tests)

| Test ID | Description | Expected |
|---------|-------------|----------|
| SEC-TMP-01 | Workspace ACL: Only current user has access | Verified via `icacls` |
| SEC-TMP-02 | Normal completion: Workspace deleted | Directory gone |
| SEC-TMP-03 | Exception during processing: Workspace deleted | Directory gone |
| SEC-TMP-04 | Process kill (Task Manager): Cleanup on next start | Stale cleanup removes it |
| SEC-TMP-05 | Power loss simulation: Cleanup on next start | Stale cleanup removes it |
| SEC-TMP-06 | Concurrent sessions: No cross-contamination | Separate GUID dirs |
| SEC-TMP-07 | Symlink in input: Not followed | Original file accessed directly |
| SEC-TMP-08 | Disk full during processing: Graceful abort | Cleanup runs, error reported |
| SEC-TMP-09 | Stale cleanup: 25-hour-old workspace removed | Deleted at startup |
| SEC-TMP-10 | Stale cleanup: Active workspace preserved | Not deleted (in use) |

---

## Verification Checklist (Phase Gate)

- [ ] Workspace isolation via GUID documented
- [ ] ACL implementation defined (current user only)
- [ ] Encryption at rest requirement documented
- [ ] Creation/usage/cleanup lifecycle defined
- [ ] Crash/exception cleanup guarantees defined
- [ ] Stale cleanup strategy defined
- [ ] Original document handling: no copy to temp
- [ ] Verification output flow defined
- [ ] Disk space limits defined
- [ ] Security test cases defined

---

*Document Version: 1.0*  
*Phase: 0*  
*Classification: INTERNAL*