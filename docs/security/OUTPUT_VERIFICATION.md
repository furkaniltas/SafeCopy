# SafeCopy — Output Verification Mandatory Acceptance Criteria

## Principle

**No masked output is delivered to the user without passing full re-scan verification.**

This is a MANDATORY phase gate. No exceptions.

---

## Verification Flow

```
Masking Engine
      │
      ▼
[Write Masked Output to Temp]
      │
      ▼
[Verification Engine: Full Re-Scan]
      │
      ├──▶ PASS: Zero residual PII → Deliver to user
      │
      └──▶ FAIL: Any residual PII → REJECT, alert user, NO DELIVERY
```

---

## Verification Requirements

### VR-01: Complete Re-Scan
The verification scan uses the **exact same detection engine** as the initial scan.
- All detectors enabled
- Same confidence thresholds
- Same context rules
- No shortcuts, no sampling

### VR-02: Zero Tolerance for Critical PII
**Critical PII Types (Zero Tolerance):**
| Type | Examples |
|------|----------|
| TC Kimlik No | `11111111111` |
| IBAN | `TR00 0000 0000 0000 0000 0000 00` |
| Vergi Kimlik No | `1234567890` |
| Pasaport No | `A12B3456` |
| Kredi Kartı | `4111 1111 1111 1111` |

**Result:** Even ONE critical PII residue → **VERIFICATION FAILED**

### VR-03: Low Tolerance for Standard PII
**Standard PII Types (Configurable Tolerance):**
| Type | Default Tolerance |
|------|-------------------|
| Ad Soyad | 0 |
| Telefon | 0 |
| E-posta | 0 |
| Adres | 0 |
| Tarih | 0 |
| Plaka | 0 |
| Tesisat/Abone/Sayaç No | 0 |

**Default:** ZERO tolerance for ALL types. Configurable only for false-positive reduction with explicit user acknowledgment.

### VR-04: Metadata Verification
Verify ALL metadata stripped:
- PDF: `/Author`, `/Title`, `/Subject`, `/Keywords`, `/Creator`, `/Producer`, `/CreationDate`, `/ModDate`, custom metadata
- DOCX: `core.xml`, `app.xml`, custom properties
- XLSX: `core.xml`, `app.xml`, custom properties, printer settings

### VR-05: Hidden Content Verification
Verify NO PII in:
- PDF: Hidden text layers, form fields, annotations, embedded files
- DOCX: Hidden text, text boxes, headers/footers, comments, footnotes, endnotes
- XLSX: Hidden sheets, hidden rows/columns, very hidden sheets, comments, notes, defined names

### VR-06: Embedded Object Verification
Recursively verify embedded objects:
- PDF: Embedded images (OCR), embedded files
- DOCX: Embedded OLE objects (skip but verify no text extraction)
- XLSX: Embedded objects

---

## Verification Engine Interface

```csharp
public interface IVerificationEngine
{
    VerificationResult Verify(string maskedOutputPath, DocumentFormat format);
}

public sealed class VerificationResult
{
    public bool Passed { get; }
    public IReadOnlyList<ResidualDetection> ResidualDetections { get; }
    public IReadOnlyList<string> MetadataIssues { get; }
    public IReadOnlyList<string> HiddenContentIssues { get; }
    public TimeSpan ScanDuration { get; }
    
    // Factory methods
    public static VerificationResult Pass(TimeSpan duration) => new(true, [], [], [], duration);
    public static VerificationResult Fail(
        IReadOnlyList<ResidualDetection> detections,
        IReadOnlyList<string> metadataIssues,
        IReadOnlyList<string> hiddenContentIssues,
        TimeSpan duration) => new(false, detections, metadataIssues, hiddenContentIssues, duration);
}

public sealed class ResidualDetection
{
    public DetectionType Type { get; }
    public string Value { get; }
    public string Context { get; }
    public double Confidence { get; }
    public BoundingBox Location { get; }
    public bool IsCritical => Type.IsCritical();
}
```

---

## Verification Implementation

```csharp
public sealed class VerificationEngine : IVerificationEngine
{
    private readonly IDetectionEngine _detectionEngine;
    private readonly IDocumentEngine _documentEngine;
    
    public VerificationResult Verify(string maskedOutputPath, DocumentFormat format)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // 1. Load masked document using SAME document engine
            var document = _documentEngine.Load(maskedOutputPath, format);
            
            // 2. Run FULL detection (all detectors, all pages)
            var detections = _detectionEngine.Detect(document);
            
            // 3. Check metadata
            var metadataIssues = CheckMetadata(maskedOutputPath, format);
            
            // 4. Check hidden content
            var hiddenContentIssues = CheckHiddenContent(document, format);
            
            // 5. Evaluate results
            var residual = detections.Where(d => d.IsSelectedForMasking).ToList();
            
            stopwatch.Stop();
            
            if (residual.Count == 0 && metadataIssues.Count == 0 && hiddenContentIssues.Count == 0)
            {
                return VerificationResult.Pass(stopwatch.Elapsed);
            }
            
            return VerificationResult.Fail(residual, metadataIssues, hiddenContentIssues, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // Verification error = FAIL (conservative)
            return VerificationResult.Fail(
                [], 
                [$"Verification error: {ex.Message}"], 
                [], 
                stopwatch.Elapsed);
        }
    }
    
    private IReadOnlyList<string> CheckMetadata(string path, DocumentFormat format)
    {
        var issues = new List<string>();
        
        switch (format)
        {
            case DocumentFormat.Pdf:
                // Check all metadata streams
                break;
            case DocumentFormat.Docx:
                // Check core.xml, app.xml, custom.xml
                break;
            case DocumentFormat.Xlsx:
                // Check core.xml, app.xml, custom.xml
                break;
        }
        
        return issues;
    }
    
    private IReadOnlyList<string> CheckHiddenContent(Document document, DocumentFormat format)
    {
        var issues = new List<string>();
        
        // Check for text in hidden layers, annotations, etc.
        // This depends on DocumentEngine exposing hidden content
        
        return issues;
    }
}
```

---

## User Experience on Verification Failure

### Scenario: Verification Fails

```
┌─────────────────────────────────────────────────────────────┐
│  ⚠ Güvenlik Doğrulaması Başarısız                           │
├─────────────────────────────────────────────────────────────┤
│  Maskelenmiş belgede hala kişisel veri tespit edildi.       │
│  Güvenli kopya oluşturulamadı.                              │
│                                                             │
│  Kalan tespitler:                                           │
│  • TC Kimlik No: 11111111111 (Sayfa 3, %98 güven)         │
│  • E-posta: user@domain.com (Sayfa 1, %95 güven)           │
│                                                             │
│  [Detayları Göster]  [Tekrar Dene]  [İptal]                │
└─────────────────────────────────────────────────────────────┘
```

**Actions:**
- **Detayları Göster**: Opens review UI with residual detections highlighted
- **Tekrar Dene**: Re-runs masking with adjusted settings (if user missed selection)
- **İptal**: Returns to document selection, no output created

### Scenario: Verification Passes

```
┌─────────────────────────────────────────────────────────────┐
│  ✓ Güvenli Kopya Hazır                                      │
├─────────────────────────────────────────────────────────────┤
│  Belge başarıyla maskelendi ve doğrulandı.                  │
│                                                             │
│  Çıktı: C:\Users\...\Documents\SafeCopy\belge_masked.pdf   │
│  Doğrulama süresi: 1.2 saniye                               │
│                                                             │
│  [Klasörü Aç]  [Başka Belge İşle]  [Kapat]                │
└─────────────────────────────────────────────────────────────┘
```

---

## Format-Specific Verification Details

### PDF Verification
| Check | Method |
|-------|--------|
| Text layer | Extract all text, run detection |
| Annotations | Check `/Annots` on each page |
| Form fields | Check `/AcroForm` and widget annotations |
| Hidden text | Check text rendering mode 3 (invisible) |
| Metadata | Check `/Info` dict and XMP metadata |
| Embedded files | Check `/EmbeddedFiles` name tree |
| Layers/OCGs | Check Optional Content Groups |

### DOCX Verification
| Check | Method |
|-------|--------|
| Body text | All paragraphs, runs |
| Headers/Footers | All section headers/footers |
| Text boxes | `w:txbxContent` |
| Comments | `w:comment` |
| Footnotes/Endnotes | `w:footnote`, `w:endnote` |
| Hidden text | `w:vanish` property |
| Metadata | `core.xml`, `app.xml`, `custom.xml` |

### XLSX Verification
| Check | Method |
|-------|--------|
| Visible cells | All worksheets, all cells |
| Hidden sheets | `state="hidden"` or `state="veryHidden"` |
| Hidden rows/cols | `hidden="1"` on row/col |
| Comments/Notes | `legacyDrawing` / `comments` parts |
| Defined names | `definedNames` with formulas |
| Metadata | `core.xml`, `app.xml`, `custom.xml` |

---

## Performance Requirements

| Metric | Target |
|--------|--------|
| Verification time (10-page PDF) | < 3 seconds |
| Verification time (50-page PDF) | < 15 seconds |
| Memory during verification | < 500 MB |
| False negative rate (critical PII) | 0% |
| False negative rate (standard PII) | < 0.1% |

---

## Test Cases (Security Tests)

| Test ID | Description | Expected |
|---------|-------------|----------|
| SEC-VER-01 | Masked PDF with TC Kimlik in text layer | FAIL — critical PII detected |
| SEC-VER-02 | Masked DOCX with email in header | FAIL — PII in header |
| SEC-VER-03 | Masked XLSX with phone in hidden sheet | FAIL — PII in hidden sheet |
| SEC-VER-04 | Masked PDF with author metadata | FAIL — metadata not stripped |
| SEC-VER-05 | Masked PDF with annotation containing name | FAIL — PII in annotation |
| SEC-VER-06 | Perfectly masked document | PASS — zero residue |
| SEC-VER-07 | Verification engine crash | FAIL — conservative |
| SEC-VER-08 | Large document (100 pages) verification | PASS/FAIL correctly, < 30s |
| SEC-VER-09 | OCR-scanned PDF with residual text under image | FAIL — text layer not removed |
| SEC-VER-10 | Document with zero-width chars evading detection | FAIL — detection handles ZWSP |

---

## Integration with Masking Pipeline

```csharp
public sealed class SafeCopyPipeline
{
    public async Task<PipelineResult> ProcessAsync(SafeCopyRequest request)
    {
        // 1. Load & Detect
        var document = _documentEngine.Load(request.InputPath, request.Format);
        var detections = _detectionEngine.Detect(document);
        
        // 2. User Review (UI)
        var userSelections = await _ui.ShowReviewAsync(detections);
        
        // 3. Mask
        var maskedDocument = _maskingEngine.Mask(document, userSelections);
        
        // 4. Write to Temp
        var tempOutput = _workspace.GetOutputPath(request.OutputName);
        _renderer.Render(maskedDocument, tempOutput, request.Format);
        
        // 5. VERIFY (MANDATORY)
        var verification = _verificationEngine.Verify(tempOutput, request.Format);
        
        if (!verification.Passed)
        {
            // Log security event
            _audit.LogVerificationFailed(request.InputPath, verification);
            
            return PipelineResult.VerificationFailed(verification);
        }
        
        // 6. Verify Original Unchanged
        _originalGuard.VerifyUnchanged();
        
        // 7. Deliver to User's Output Location
        File.Copy(tempOutput, request.UserOutputPath, overwrite: false);
        
        // 8. Cleanup
        _workspace.Dispose();
        
        return PipelineResult.Success(request.UserOutputPath, verification);
    }
}
```

---

## Verification Checklist (Phase Gate)

- [ ] Verification flow documented and mandatory
- [ ] Complete re-scan with same engine required
- [ ] Zero tolerance for critical PII defined
- [ ] Metadata verification specified per format
- [ ] Hidden content verification specified per format
- [ ] Embedded object verification strategy defined
- [ ] Verification engine interface designed
- [ ] User experience for pass/fail defined
- [ ] Format-specific verification details documented
- [ ] Performance targets set
- [ ] Security test cases defined
- [ ] Pipeline integration designed

---

*Document Version: 1.0*  
*Phase: 0*  
*Classification: INTERNAL*