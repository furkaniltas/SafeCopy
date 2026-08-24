# Eksim SafeCopy — OCR Attack Surface Evaluation

## Overview

OCR engine processes images (scanned PDF pages, standalone images). This document evaluates attack vectors specific to image processing and OCR.

---

## Threat Model Assumptions

- OCR runs **locally only** — no cloud APIs
- OCR models **bundled with application** — no runtime downloads
- OCR processes **untrusted images** from user documents
- OCR engine: **Tesseract** (via wrapper) or **Windows.Media.Ocr** (Windows 11 built-in)

---

## Attack Vectors

### 1. Image Decompression Bombs

**Attack:** Small compressed image (PNG, JPEG, TIFF) expands to massive pixel dimensions, exhausting memory.

| Format | Risk | Example |
|--------|------|---------|
| PNG | High | 100x100 PNG with huge IDAT chunks → 50000x50000 pixels |
| JPEG | Medium | Progressive JPEG with huge dimensions |
| TIFF | High | Multiple strips/tiles, large dimensions |
| BMP | Low | Uncompressed, size obvious |

**Mitigations:**
- **Decode limits:** Max 8192x8192 pixels (configurable)
- **Max decoded size:** 100 MP (megapixels) per image
- **Streaming decode:** Validate dimensions before full decode
- **Memory budget:** Per-image memory limit (e.g., 200 MB)
- **Timeout:** Hard timeout on decode (e.g., 30 seconds)

### 2. Malformed Image Parsing Exploits

**Attack:** Crafted images exploiting vulnerabilities in image decoders (libpng, libjpeg, etc.).

**Mitigations:**
- Use **Windows built-in decoders** (`System.Drawing`, `Windows.Graphics.Imaging`, WPF `BitmapDecoder`)
- Keep Windows updated (OS-level decoder patches)
- **No third-party native image decoders** unless absolutely necessary
- If third-party: pin version, monitor CVEs, run in isolated process

### 3. Adversarial Images (ML/OCR Evasion)

**Attack:** Images designed to:
- Cause OCR misclassification (miss text, hallucinate text)
- Trigger OCR engine crashes/hangs
- Exfiltrate data via OCR output (if output sent externally — **NOT APPLICABLE**)

**Mitigations:**
- **Confidence thresholds:** Low-confidence results flagged for review
- **Multiple preprocessing pipelines:** Deskew, denoise, threshold variants
- **Timeout/cancellation:** Hard limits on OCR processing time
- **Output validation:** Verify OCR output structure makes sense
- **No automatic trust:** All OCR results go through same detection + review pipeline

### 4. Embedded Content in Images

**Attack:** Images containing:
- Steganographic data (irrelevant — not extracted)
- QR codes/barcodes with malicious URLs (not executed)
- Text that looks like code/commands (treated as text only)

**Mitigations:**
- OCR extracts **text only** — no barcode/QR decoding
- No execution of any extracted content
- Text goes through standard detection pipeline

### 5. Scanned PDF Image Extraction

**Attack:** PDF with malicious embedded images.

**Mitigations:**
- PDF image extraction uses same image pipeline
- Each extracted image subject to same limits
- **Embedded image count limit:** 100 images per PDF
- **Total extracted image size limit:** 200 MB

---

## OCR Engine Options Evaluation

### Option A: Tesseract (via Tesseract.NET wrapper)

| Aspect | Assessment |
|--------|------------|
| Local-only | ✅ Yes |
| Turkish support | ✅ Yes (traineddata) |
| Model bundling | ✅ Yes |
| License | Apache 2.0 (permissive) |
| Native dependency | ⚠️ Requires `tesseract.dll` + `leptonica.dll` |
| Attack surface | Larger (C++ codebase) |
| Isolation | Can run in separate process |
| Performance | Good |

**Security Notes:**
- Native DLLs increase attack surface
- Must bundle specific version, verify hashes
- Consider process isolation for untrusted images

### Option B: Windows.Media.Ocr (Windows 11 Built-in)

| Aspect | Assessment |
|--------|------------|
| Local-only | ✅ Yes |
| Turkish support | ✅ Yes (language pack) |
| Model bundling | ✅ OS-provided |
| License | ✅ Part of Windows |
| Native dependency | ✅ None (managed API) |
| Attack surface | Minimal (OS component) |
| Isolation | In-process |
| Performance | Good, hardware accelerated |

**Security Notes:**
- Smallest attack surface (no extra binaries)
- OS handles image decoding securely
- Language packs must be installed (Windows 11 includes Turkish)
- **Preferred option** for security

### Option C: Custom ONNX Model (ML.NET / ONNX Runtime)

| Aspect | Assessment |
|--------|------------|
| Local-only | ✅ Yes |
| Turkish support | ❌ Need custom training |
| Model bundling | ✅ Yes |
| License | ✅ MIT/Apache |
| Native dependency | ⚠️ ONNX Runtime native |
| Attack surface | Medium |
| Isolation | Possible |
| Performance | Good with GPU |

**Security Notes:**
- Requires significant ML expertise
- Training data for Turkish PII documents needed
- **Not recommended for MVP** — Phase 18+

---

## Recommended Architecture

### Primary: Windows.Media.Ocr (Windows 11)

```csharp
// OcrEngine interface
public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(Stream imageStream, CancellationToken ct);
    bool IsAvailable { get; }
}

// Windows implementation
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine _engine;
    
    public WindowsOcrEngine()
    {
        _engine = OcrEngine.TryCreateFromLanguage(new Language("tr"));
    }
    
    public bool IsAvailable => _engine != null;
    
    public async Task<OcrResult> RecognizeAsync(Stream imageStream, CancellationToken ct)
    {
        // 1. Validate image dimensions via BitmapDecoder (no full decode)
        // 2. Decode to SoftwareBitmap with size limits
        // 3. Run OCR with timeout
        // 4. Map results to common OcrResult model
    }
}
```

### Fallback: Tesseract (if Windows OCR unavailable)

```csharp
// Only if Windows.Media.Ocr not available (unlikely on Win 11)
public sealed class TesseractOcrEngine : IOcrEngine
{
    // Run in isolated process via named pipes / gRPC
    // Strict timeouts, memory limits via Job Objects
}
```

---

## Image Preprocessing Pipeline (Security-First)

```csharp
public sealed class SecureImagePreprocessor
{
    public const int MaxDimension = 8192;
    public const long MaxPixels = 100_000_000; // 100 MP
    public const long MaxFileSize = 50_000_000; // 50 MB compressed
    
    public SoftwareBitmap Preprocess(Stream input, CancellationToken ct)
    {
        // 1. Validate file size BEFORE decode
        if (input.Length > MaxFileSize) throw new ImageTooLargeException();
        
        // 2. Decode header only to get dimensions
        var decoder = BitmapDecoder.Create(input.AsRandomAccessStream(), 
            BitmapDecoderOptions.None);
        var frame = await decoder.GetFrameAsync(0);
        
        if (frame.PixelWidth > MaxDimension || frame.PixelHeight > MaxDimension)
            throw new ImageDimensionsExceededException();
        
        if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels)
            throw new ImageTooManyPixelsException();
        
        // 3. Full decode with memory limit
        var bitmap = await frame.GetSoftwareBitmapAsync();
        
        // 4. Preprocessing (deskew, denoise, contrast) — all in-memory
        //    Each step validates output dimensions unchanged
        
        return bitmap;
    }
}
```

---

## Test Cases (Security Tests)

| Test ID | Description | Expected |
|---------|-------------|----------|
| SEC-OCR-01 | 50000x50000 PNG (decompression bomb) | Rejected at header validation |
| SEC-OCR-02 | Malformed PNG with invalid chunks | Handled by OS decoder, no crash |
| SEC-OCR-03 | JPEG with 100:1 compression ratio | Rejected at size validation |
| SEC-OCR-04 | Image causing Tesseract segfault (if used) | Process isolation contains crash |
| SEC-OCR-05 | Adversarial image (text evasion) | Low confidence, flagged for review |
| SEC-OCR-06 | PDF with 200 embedded images | Only first 100 processed |
| SEC-OCR-07 | Image with embedded EXIF GPS data | EXIF ignored, not extracted |
| SEC-OCR-08 | OCR timeout (30s) | Cancelled, partial results discarded |
| SEC-OCR-09 | Corrupt TIFF with multiple IFDs | Handled by OS decoder |
| SEC-OCR-10 | Windows OCR language pack missing | Graceful fallback / clear error |

---

## Residual Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Zero-day in Windows imaging stack | Low | High | OS patches, no third-party decoders |
| OCR hallucinates PII where none exists | Medium | Medium | Confidence thresholds, human review |
| OCR misses PII in low-quality scan | Medium | High | Multiple preprocessing passes, user review |
| Language pack not installed | Low | Medium | Clear error, documentation |

---

## Verification Checklist (Phase Gate)

- [ ] Image decompression bomb mitigations documented
- [ ] Malformed image handling defined
- [ ] Adversarial image considerations documented
- [ ] OCR engine selection justified (Windows.Media.Ocr preferred)
- [ ] Preprocessing pipeline security controls defined
- [ ] Size/dimension/timeouts specified
- [ ] Process isolation strategy for fallback engine
- [ ] Security test cases defined
- [ ] Residual risks acknowledged

---

*Document Version: 1.0*  
*Phase: 0*  
*Classification: INTERNAL*