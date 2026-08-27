namespace EksimSafeCopy.Detectors.Detection.Context;

using EksimSafeCopy.Core.Models;

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
        return beforeContext.Contains(label, comparison) || afterContext.Contains(label, comparison);
    }

    public double GetLabelProximityScore(string label, int maxDistance = 50)
    {
        var beforeContext = GetBeforeContext(maxDistance);
        var afterContext = GetAfterContext(maxDistance);
        
        var beforeIndex = beforeContext.LastIndexOf(label, StringComparison.OrdinalIgnoreCase);
        var afterIndex = afterContext.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        
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