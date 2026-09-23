namespace SafeCopy.Detectors.Detection.Context;

using SafeCopy.Core.Models;

public sealed class ContextWindow
{
    public string BeforeText { get; init; } = string.Empty;
    public string MatchText { get; init; } = string.Empty;
    public string AfterText { get; init; } = string.Empty;
    public int BeforeStartIndex { get; init; }
    public int MatchStartIndex { get; init; }
    public int AfterEndIndex { get; init; }

    public string FullText => BeforeText + MatchText + AfterText;

    public static ContextWindow Create(string fullText, int matchStart, int matchLength, int windowSize = 100)
    {
        var beforeStart = Math.Max(0, matchStart - windowSize);
        var beforeLength = matchStart - beforeStart;
        var afterStart = matchStart + matchLength;
        var afterEnd = Math.Min(fullText.Length, afterStart + windowSize);

        return new ContextWindow
        {
            BeforeText = fullText.Substring(beforeStart, beforeLength),
            MatchText = fullText.Substring(matchStart, matchLength),
            AfterText = fullText.Substring(afterStart, afterEnd - afterStart),
            BeforeStartIndex = beforeStart,
            MatchStartIndex = matchStart,
            AfterEndIndex = afterEnd
        };
    }

    public string GetBeforeContext(int length)
    {
        if (length >= BeforeText.Length) return BeforeText;
        return BeforeText.Substring(BeforeText.Length - length);
    }

    public string GetAfterContext(int length)
    {
        if (length >= AfterText.Length) return AfterText;
        return AfterText.Substring(0, length);
    }

    public bool HasLabelNearby(string label, int maxDistance = 50, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var beforeContext = GetBeforeContext(maxDistance);
        var afterContext = GetAfterContext(maxDistance);
        return ContainsWholeWord(beforeContext, label) || ContainsWholeWord(afterContext, label);
    }

    private static bool ContainsWholeWord(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label)) return false;
        // Use word-boundary regex with Turkish-aware case-insensitive check via split
        var tr = new System.Globalization.CultureInfo("tr-TR");
        var lowerText = text.ToLower(tr);
        var lowerLabel = label.ToLower(tr);
        if (!lowerLabel.Contains(' '))
        {
            var words = lowerText.Split(new[] { ' ', '\t', '.', ',', ';', ':', '!', '?', '\n', '\r', '"', '\'', '’', '‘', '(', ')', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in words)
            {
                var clean = w.TrimEnd('.', ',', ';', ':', '!', '?', '"', '\'', '’', '‘', ')', '(', '-', '/');
                if (clean == lowerLabel) return true;
            }
            return false;
        }
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(lowerLabel)}\b";
        return System.Text.RegularExpressions.Regex.IsMatch(lowerText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static int LastIndexOfWholeWord(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label)) return -1;
        var tr = new System.Globalization.CultureInfo("tr-TR");
        var lowerText = text.ToLower(tr);
        var lowerLabel = label.ToLower(tr);
        if (!lowerLabel.Contains(' '))
        {
            // Find last whole-word occurrence via word split with positions
            var words = System.Text.RegularExpressions.Regex.Matches(lowerText, @"\b\w+\b");
            int lastIdx = -1;
            foreach (System.Text.RegularExpressions.Match m in words)
            {
                if (m.Value == lowerLabel) lastIdx = m.Index;
            }
            return lastIdx;
        }
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(lowerLabel)}\b";
        var matches = System.Text.RegularExpressions.Regex.Matches(lowerText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (matches.Count == 0) return -1;
        return matches[^1].Index;
    }

    private static int IndexOfWholeWord(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label)) return -1;
        var tr = new System.Globalization.CultureInfo("tr-TR");
        var lowerText = text.ToLower(tr);
        var lowerLabel = label.ToLower(tr);
        if (!lowerLabel.Contains(' '))
        {
            var m = System.Text.RegularExpressions.Regex.Match(lowerText, $@"\b{System.Text.RegularExpressions.Regex.Escape(lowerLabel)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Index : -1;
        }
        var match = System.Text.RegularExpressions.Regex.Match(lowerText, $@"\b{System.Text.RegularExpressions.Regex.Escape(lowerLabel)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Index : -1;
    }

    public double GetLabelProximityScore(string label, int maxDistance = 50)
    {
        var beforeContext = GetBeforeContext(maxDistance);
        var afterContext = GetAfterContext(maxDistance);
        
        var beforeIndex = LastIndexOfWholeWord(beforeContext, label);
        var afterIndex = IndexOfWholeWord(afterContext, label);
        
        if (beforeIndex >= 0)
        {
            var distance = beforeContext.Length - beforeIndex - label.Length;
            return Math.Max(0, 1.0 - (double)distance / maxDistance);
        }
        
        if (afterIndex >= 0)
        {
            var distance = afterIndex;
            return Math.Max(0, 1.0 - (double)distance / maxDistance);
        }
        
        return 0;
    }
}

public sealed class ContextAnalyzer
{
    private static readonly string[] TurkishNameLabels = 
    {
        "ad soyad", "adı soyadı", "adı", "soyadı", "isim", "tam isim",
        "müşteri", "başvuru sahibi", "hak sahibi", "tesis sahibi",
        "vatandaş", "ilgili kişi", "sahip", "malik"
    };

    private static readonly string[] TurkishTcLabels = 
    {
        "t.c. kimlik", "tc kimlik", "tckn", "kimlik no", "kimlik numarası",
        "tc no", "kimlik", "tc"
    };

    private static readonly string[] TurkishPhoneLabels = 
    {
        "telefon", "gsm", "cep telefonu", "cep", "tel", "telefon no",
        "iletişim", "mobil"
    };

    private static readonly string[] TurkishEmailLabels = 
    {
        "e-posta", "eposta", "email", "e-mail", "mail", "e posta"
    };

    private static readonly string[] TurkishDateLabels = 
    {
        "doğum tarihi", "doğum t.", "doğum", "dogum tarihi", "dtarih"
    };

    private static readonly string[] TurkishAddressLabels = 
    {
        "adres", "açık adres", "ikametgah", "ikamet", "yerleşim yeri"
    };

    private static readonly string[] TurkishInstallationLabels = 
    {
        "tesisat no", "tesisat numarası", "tesisat numara", "tesisat",
        "sayaç no", "sayaç numarası", "abone no", "abone numarası",
        "sözleşme no", "müşteri no", "dosya no", "başvuru no"
    };

    private static readonly string[] NegativeNameContexts = 
    {
        "şirket", "firma", "kurum", "kuruluş", "müdürlük", "birim",
        "bölge", "bölüm", "departman", "üniversite", "okul", "hastane",
        "belediye", "valilik", "kaymakamlık", "mahkeme", "polis",
        "elektrik", "su", "doğalgaz", "internet", "telekom",
        "dağıtım", "dağıtım", "tedarik", "hizmet", "destek"
    };

    private static readonly string[] NegativeAddressContexts = 
    {
        "şirket adresi", "firma adresi", "kurum adresi", "resmi adres",
        "kayıtlı adres", "merkez adresi", "şube adresi", "ofis adresi"
    };

    public ContextFeatures Analyze(ContextWindow window, DetectionType detectionType)
    {
        var features = new ContextFeatures();

        switch (detectionType)
        {
            case DetectionType.FullName:
                features.LabelScore = GetMaxLabelScore(window, TurkishNameLabels);
                features.NegativeLabelScore = GetMaxLabelScore(window, NegativeNameContexts);
                features.HasStrongLabel = features.LabelScore > 0.7;
                features.HasNegativeLabel = features.NegativeLabelScore > 0.5;
                break;

            case DetectionType.TcKimlikNo:
                features.LabelScore = GetMaxLabelScore(window, TurkishTcLabels, 200);
                features.HasStrongLabel = features.LabelScore > 0.5;
                break;

            case DetectionType.Phone:
                features.LabelScore = GetMaxLabelScore(window, TurkishPhoneLabels);
                features.HasStrongLabel = features.LabelScore > 0.6;
                break;

            case DetectionType.Email:
                features.LabelScore = GetMaxLabelScore(window, TurkishEmailLabels);
                features.HasStrongLabel = features.LabelScore > 0.6;
                break;

            case DetectionType.Date:
                features.LabelScore = GetMaxLabelScore(window, TurkishDateLabels);
                features.HasStrongLabel = features.LabelScore > 0.6;
                break;

            case DetectionType.Address:
                features.LabelScore = GetMaxLabelScore(window, TurkishAddressLabels);
                features.NegativeLabelScore = GetMaxLabelScore(window, NegativeAddressContexts);
                features.HasStrongLabel = features.LabelScore > 0.6;
                features.HasNegativeLabel = features.NegativeLabelScore > 0.5;
                break;

            case DetectionType.TesisatNo:
                features.LabelScore = GetMaxLabelScore(window, TurkishInstallationLabels);
                features.HasStrongLabel = features.LabelScore > 0.6;
                break;
        }

        return features;
    }

    private double GetMaxLabelScore(ContextWindow window, string[] labels, int maxDistance = 50)
    {
        double maxScore = 0;
        foreach (var label in labels)
        {
            var score = window.GetLabelProximityScore(label, maxDistance);
            if (score > maxScore) maxScore = score;
        }
        return maxScore;
    }
}

public sealed class ContextFeatures
{
    public double LabelScore { get; set; }
    public double NegativeLabelScore { get; set; }
    public bool HasStrongLabel { get; set; }
    public bool HasNegativeLabel { get; set; }
}