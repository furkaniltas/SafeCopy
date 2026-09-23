# SafeCopy — Original Document Immutability Guarantee

## Principle

**The original document is NEVER modified.** This is a non-negotiable security requirement.

---

## Guarantees

| Guarantee | Implementation | Verification |
|-----------|----------------|--------------|
| Original file not opened for write | `FileAccess.Read`, `FileShare.Read` | Code review, static analysis |
| Original file not deleted | No `File.Delete` on original path | Code review |
| Original file not moved/renamed | No `File.Move` on original path | Code review |
| Original file attributes unchanged | No `File.SetAttributes` on original | Code review |
| Original file timestamps unchanged | Read-only access doesn't modify | OS behavior |
| Original file content unchanged | Hash verification before/after | Runtime verification |
| Original file not locked exclusively | `FileShare.Read` allows other readers | Code review |

---

## Implementation Requirements

### 1. File Access Pattern

```csharp
// CORRECT: Read-only, shared read
using var stream = File.Open(
    originalPath, 
    FileMode.Open, 
    FileAccess.Read, 
    FileShare.Read);  // Allow other processes to read

// FORBIDDEN: Any write access
// File.Open(originalPath, FileMode.Open, FileAccess.ReadWrite, ...)
// File.Open(originalPath, FileMode.Create, ...)
// File.OpenWrite(originalPath)
```

### 2. Hash Verification (Mandatory)

```csharp
public sealed class OriginalDocumentGuard : IDisposable
{
    private readonly string _path;
    private readonly byte[] _originalHash;
    private readonly FileInfo _originalInfo;
    
    public OriginalDocumentGuard(string path)
    {
        _path = path;
        _originalInfo = new FileInfo(path);
        _originalHash = ComputeSha256(path);
    }
    
    public void VerifyUnchanged()
    {
        // 1. File still exists
        if (!File.Exists(_path))
            throw new OriginalDocumentMissingException(_path);
        
        // 2. Size unchanged
        var currentInfo = new FileInfo(_path);
        if (currentInfo.Length != _originalInfo.Length)
            throw new OriginalDocumentModifiedException(_path, "Size changed");
        
        // 3. Hash unchanged
        var currentHash = ComputeSha256(_path);
        if (!CryptographicOperations.FixedTimeEquals(_originalHash, currentHash))
            throw new OriginalDocumentModifiedException(_path, "Content changed (hash mismatch)");
        
        // 4. LastWriteTime unchanged (defense in depth)
        if (currentInfo.LastWriteTimeUtc != _originalInfo.LastWriteTimeUtc)
            throw new OriginalDocumentModifiedException(_path, "Timestamp changed");
    }
    
    public void Dispose()
    {
        // Final verification on disposal
        VerifyUnchanged();
    }
    
    private static byte[] ComputeSha256(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return sha.ComputeHash(fs);
    }
}
```

**Usage:**
```csharp
using var guard = new OriginalDocumentGuard(userSelectedPath);
try
{
    // All processing here
    var document = documentEngine.Load(userSelectedPath);
    var detections = detectionEngine.Detect(document);
    var masked = maskingEngine.Mask(document, userSelections);
    var outputPath = Path.Combine(outputDir, "masked_" + Path.GetFileName(userSelectedPath));
    renderer.Render(masked, outputPath);
    
    // Verification
    var verification = verificationEngine.Verify(outputPath);
    if (!verification.Passed)
        throw new VerificationFailedException(verification);
}
finally
{
    // Guard.Dispose() verifies original unchanged
}
```

### 3. Library Constraints

Some libraries (PDF, Office) may require write access or file locking.

**Strategy:**
1. **Prefer libraries supporting read-only streams** (e.g., `PdfDocument.Open(stream)`)
2. **If library requires file path:** Create hard link in temp workspace (same volume only)
3. **If hard link not possible:** Read-only copy to temp, process copy, **never original**

```csharp
// For libraries requiring file path
string GetLibrarySafePath(string originalPath, SecureTempWorkspace workspace)
{
    // Try hard link first (instant, no space, same inode)
    var linkPath = Path.Combine(workspace.InputPath, Path.GetFileName(originalPath));
    try
    {
        if (CreateHardLink(linkPath, originalPath, IntPtr.Zero))
            return linkPath;
    }
    catch { /* Fall through to copy */ }
    
    // Fallback: Read-only copy (last resort)
    var copyPath = Path.Combine(workspace.InputPath, "copy_" + Path.GetFileName(originalPath));
    File.Copy(originalPath, copyPath);
    File.SetAttributes(copyPath, FileAttributes.ReadOnly);
    return copyPath;
}
```

---

## Static Analysis Rules

**Enforce via Roslyn Analyzer / Code Review:**

| Rule ID | Description | Severity |
|---------|-------------|----------|
| ESKIM-IMM-001 | `FileAccess.Write` or `ReadWrite` on user document path | Error |
| ESKIM-IMM-002 | `FileMode.Create`, `CreateNew`, `Append`, `Truncate` on user path | Error |
| ESKIM-IMM-003 | `File.Delete`, `File.Move`, `File.Replace` on user path | Error |
| ESKIM-IMM-004 | `File.SetAttributes` on user path | Error |
| ESKIM-IMM-005 | `File.OpenWrite`, `File.Create`, `File.AppendText` on user path | Error |
| ESKIM-IMM-006 | Missing `OriginalDocumentGuard` for user document processing | Warning |

---

## Test Cases (Security Tests)

| Test ID | Description | Expected |
|---------|-------------|----------|
| SEC-IMM-01 | Process read-only PDF — original unchanged | Hash matches, no write |
| SEC-IMM-02 | Process DOCX with library requiring file path — original unchanged | Hard link or copy used, original hash matches |
| SEC-IMM-03 | Crash during processing — original unchanged | Hash matches after restart |
| SEC-IMM-04 | Power loss simulation — original unchanged | Hash matches after reboot |
| SEC-IMM-05 | Concurrent access: Other app reads original during processing | No sharing violation |
| SEC-IMM-06 | Original file on network share — processed read-only | Works, no write attempt |
| SEC-IMM-07 | Original file marked read-only by OS — processing succeeds | Read-only access works |
| SEC-IMM-08 | Malicious library attempt to write — blocked by guard | Exception thrown, original safe |
| SEC-IMM-09 | Original file deleted externally during processing — detected | `OriginalDocumentMissingException` |
| SEC-IMM-10 | Hash verification performance — < 100ms for 100MB file | Meets SLA |

---

## Error Handling

| Exception | User Message | Action |
|-----------|--------------|--------|
| `OriginalDocumentMissingException` | "Orijinal belge bulunamadı. İşlem iptal edildi." | Abort, no output |
| `OriginalDocumentModifiedException` | "GÜVENLİK HATASI: Orijinal belge değiştirildi. İşlem iptal edildi." | Abort, alert, log security event |
| `VerificationFailedException` | "Güvenlik doğrulaması başarısız. Maske işlemi tamamlanamadı." | Abort, no output delivered |

---

## Verification Checklist (Phase Gate)

- [ ] Read-only file access pattern mandated
- [ ] `OriginalDocumentGuard` with SHA-256 verification designed
- [ ] Library constraint strategy defined (hard link > copy > reject)
- [ ] Static analysis rules specified
- [ ] Error types and user messages defined
- [ ] Security test cases defined
- [ ] All guarantees mapped to implementation

---

*Document Version: 1.0*  
*Phase: 0*  
*Classification: INTERNAL*