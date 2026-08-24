# Eksim SafeCopy — ZIP/XML Attack Surface Evaluation

## Overview

DOCX, XLSX, and other OOXML formats are ZIP packages containing XML. This document evaluates attack vectors and defines mitigations.

---

## ZIP-Based Attacks

### 1. ZIP Bomb (Decompression Bomb)

**Attack:** Small compressed file expands to massive size, exhausting disk/memory.

**Examples:**
- `42.zip` (42 KB → 4.5 PB)
- Nested ZIPs within OOXML package

**Mitigations:**
- **Max uncompressed size per entry:** 100 MB (configurable, default)
- **Max total uncompressed size:** 500 MB (configurable, default)
- **Compression ratio limit:** 100:1 (reject if exceeded)
- **Streaming extraction:** Never fully extract to disk; process streams directly
- **Entry count limit:** 10,000 entries max per package

### 2. Path Traversal (Zip Slip)

**Attack:** Malicious entry names with `../` escape extraction directory.

**Example:**
```zip
../../Windows/System32/evil.dll
```

**Mitigations:**
- Canonicalize all entry paths before extraction
- Validate path stays within target workspace directory
- Reject entries with absolute paths or drive letters
- Use `Path.GetFullPath` + `StartsWith(workspaceRoot)` validation

### 3. Symlink/Reparse Point Attacks

**Attack:** ZIP entries that are symlinks pointing outside workspace.

**Mitigations:**
- Do not follow symlinks during extraction
- Detect reparse points (`FileAttributes.ReparsePoint`)
- Skip or reject entries that are links
- Extract only regular files

### 4. Nested Archives

**Attack:** ZIP within ZIP within DOCX, bypassing size limits.

**Mitigations:**
- Do not recursively extract nested archives
- Treat nested archives as opaque binary blobs
- If nested archive detection needed for content, process with same limits

### 5. Central Directory Manipulation

**Attack:** Mismatch between local file headers and central directory.

**Mitigations:**
- Use robust ZIP library (e.g., `System.IO.Compression.ZipArchive`)
- Validate consistency between headers
- Reject packages with mismatched metadata

---

## XML-Based Attacks (OOXML)

### 1. XML External Entity (XXE) Injection

**Attack:** DTD with external entities reading local files or making network requests.

```xml
<!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
<root>&xxe;</root>
```

**Mitigations:**
- **Disable DTD processing entirely:** `XmlReaderSettings.DtdProcessing = DtdProcessing.Prohibit`
- **Disable XML resolver:** `XmlReaderSettings.XmlResolver = null`
- **No inline DTD:** Reject documents with DOCTYPE declarations
- **Use `XmlReader` not `XmlDocument`** for streaming, non-validating parsing

### 2. XML Entity Expansion (Billion Laughs)

**Attack:** Recursive entity expansion consuming CPU/memory.

```xml
<!ENTITY lol "lol">
<!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
...
```

**Mitigations:**
- DTD processing disabled (see XXE)
- If DTD ever needed: `XmlReaderSettings.MaxCharactersFromEntities = 10000`

### 3. XInclude Processing

**Attack:** `<xi:include href="file:///etc/passwd" />` includes external resources.

**Mitigations:**
- Disable XInclude: `XmlReaderSettings.DtdProcessing = DtdProcessing.Prohibit`
- No custom `XmlResolver` that resolves external URIs

### 4. XML Schema Validation Attacks

**Attack:** Malicious schemas causing DoS during validation.

**Mitigations:**
- **No schema validation** during ingestion
- Parse as well-formed XML only
- Validation (if needed) done on extracted logical model, not raw XML

### 5. Large XML Documents

**Attack:** Massive XML elements/attributes causing OOM.

**Mitigations:**
- Streaming `XmlReader` (not DOM)
- Element depth limit: 256 levels
- Attribute count limit per element: 1000
- Text node length limit: 10 MB per node

---

## Format-Specific Evaluations

### DOCX (OOXML)

| Component | Risk | Mitigation |
|-----------|------|------------|
| `word/document.xml` | XXE, Entity expansion | DTD disabled, streaming reader |
| `word/header*.xml`, `footer*.xml` | Same as document | Same mitigation |
| `word/footnotes.xml`, `endnotes.xml` | Same | Same |
| `word/comments.xml` | Same, plus author info | Same, strip author metadata on output |
| `word/numbering.xml`, `styles.xml` | Large but low risk | Size limits |
| `word/media/*` (embedded images) | Image bombs | Process via image pipeline with limits |
| `word/embeddings/*` (OLE objects) | Arbitrary binaries | **Ignore/skip** — do not process |
| `docProps/core.xml`, `app.xml` | Metadata leakage | Strip on output, don't trust on input |
| `[Content_Types].xml` | Package structure | Validate minimal required content types |
| `_rels/.rels` | Relationship manipulation | Validate relationships point within package |

### XLSX (OOXML)

| Component | Risk | Mitigation |
|-----------|------|------------|
| `xl/workbook.xml` | Sheet references | Validate sheet names, no external refs |
| `xl/worksheets/sheet*.xml` | Cell data, formulas | Treat formulas as text, no evaluation |
| `xl/sharedStrings.xml` | Large string table | Streaming read, size limits |
| `xl/styles.xml` | Formatting | Minimal parsing |
| `xl/charts/*`, `xl/drawings/*` | Complex XML | Skip/ignore for text extraction |
| `xl/vbaProject.bin` | Macros | **Ignore** — never execute |
| `xl/externalLinks/*` | External references | Ignore |

### UDF (Universal Disk Format)

**Note:** UDF is an optical media filesystem, not ZIP/XML. Included for completeness.

| Risk | Mitigation |
|------|------------|
| Malformed filesystem structures | Robust UDF parser, bounds checking |
| Partition table manipulation | Read-only access, no partition parsing |
| Long filename/Unicode issues | Normalize paths, validate encoding |
| Sparse files / alternate data streams | Ignore ADS, process primary stream only |

---

## Implementation Requirements

### ZIP Processing (DocumentEngine)

```csharp
// Required configuration
public class ZipSecurityOptions
{
    public long MaxEntrySize { get; set; } = 100_000_000;      // 100 MB
    public long MaxTotalSize { get; set; } = 500_000_000;      // 500 MB
    public int MaxCompressionRatio { get; set; } = 100;         // 100:1
    public int MaxEntryCount { get; set; } = 10_000;
    public bool AllowSymlinks { get; set; } = false;
}

// Required validation
public static void ValidateZipEntry(ZipArchiveEntry entry, string workspaceRoot)
{
    // 1. No absolute paths
    if (Path.IsPathRooted(entry.FullName)) throw new SecurityException();
    
    // 2. No path traversal
    string fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, entry.FullName));
    if (!fullPath.StartsWith(workspaceRoot, StringComparison.Ordinal)) 
        throw new SecurityException();
    
    // 3. No reparse points
    if ((entry.ExternalAttributes & 0x400) != 0) // FILE_ATTRIBUTE_REPARSE_POINT
        throw new SecurityException();
    
    // 4. Size limits
    if (entry.Length > Options.MaxEntrySize) throw new SecurityException();
    if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > Options.MaxCompressionRatio)
        throw new SecurityException();
}
```

### XML Processing (DocumentEngine)

```csharp
// Required XmlReaderSettings for ALL OOXML parsing
public static XmlReaderSettings CreateSecureXmlReaderSettings()
{
    return new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        MaxCharactersFromEntities = 0, // Not used since DTD prohibited
        // Custom limits via custom XmlReader wrapper if needed
    };
}
```

---

## Test Cases (Security Tests)

| Test ID | Description | Expected |
|---------|-------------|----------|
| SEC-ZIP-01 | 42.zip style bomb (100:1 ratio) | Rejected at ingestion |
| SEC-ZIP-02 | Entry with `../../etc/passwd` | Rejected, no file written |
| SEC-ZIP-03 | Symlink entry in ZIP | Rejected/skipped |
| SEC-ZIP-04 | Nested ZIP in DOCX | Not recursively extracted |
| SEC-ZIP-05 | 10,001 entries in package | Rejected |
| SEC-XML-01 | XXE with file:// entity | Rejected (DTD prohibited) |
| SEC-XML-02 | Billion laughs entity expansion | Rejected (DTD prohibited) |
| SEC-XML-03 | XInclude external reference | Rejected |
| SEC-XML-04 | 10 MB text node in XML | Handled via streaming |
| SEC-XML-05 | DOCTYPE declaration present | Rejected |
| SEC-DOCX-01 | DOCX with vbaProject.bin | Ignored, no execution |
| SEC-DOCX-02 | DOCX with external relationship | Ignored |
| SEC-XLSX-01 | XLSX with formula `=HYPERLINK(...)` | Treated as text |
| SEC-XLSX-02 | XLSX with hidden sheets | All sheets processed |

---

## Verification Checklist (Phase Gate)

- [ ] ZIP bomb mitigations documented and implemented
- [ ] Path traversal protection implemented
- [ ] Symlink/reparse point handling implemented
- [ ] Nested archive handling defined
- [ ] XXE mitigations implemented (DTD prohibited)
- [ ] Entity expansion mitigations implemented
- [ ] XInclude disabled
- [ ] XML size/depth limits enforced
- [ ] Format-specific risks addressed
- [ ] Security test cases defined

---

*Document Version: 1.0*  
*Phase: 0*  
*Classification: INTERNAL*