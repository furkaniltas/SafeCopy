using EksimSafeCopy.Core.Models;

namespace EksimSafeCopy.App.ViewModels;

public sealed class DetectionItemViewModel : ViewModelBase
{
    private bool _isSelected;
    private readonly Detection _detection;

    public DetectionItemViewModel(Detection detection, bool isSelected = true)
    {
        _detection = detection ?? throw new ArgumentNullException(nameof(detection));
        _isSelected = isSelected;
        _detection.State = isSelected ? DetectionState.Selected : DetectionState.Deselected;
    }

    public Detection Detection => _detection;

    public string Id => _detection.Id;

    public DetectionType Type => _detection.Type;

    public string DisplayType => FormatDisplayType(_detection.Type);

    public string Value => _detection.Value;

    public string Text => _detection.Value;

    public string Context => _detection.Context;

    public double Confidence => _detection.Confidence;

    public ConfidenceLevel ConfidenceLevel => _detection.ConfidenceLevel;

    public string ConfidenceText => MapConfidence(_detection.ConfidenceLevel);

    public int PageNumber => _detection.PageNumber;

    public BoundingBox Location => _detection.Location;

    public CoordinateSystem? CoordinateSystem => _detection.Location.IsEmpty ? null : null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _detection.State = value ? DetectionState.Selected : DetectionState.Deselected;
                OnPropertyChanged(nameof(DisplayType));
            }
        }
    }

    private static string MapConfidence(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.Low => "Low",
        ConfidenceLevel.Medium => "Medium",
        ConfidenceLevel.High => "High",
        ConfidenceLevel.Critical => "High",
        _ => "Medium"
    };

    private static string FormatDisplayType(DetectionType type) => type switch
    {
        DetectionType.TcKimlikNo => "TC Kimlik No",
        DetectionType.Phone => "Telefon",
        DetectionType.Email => "E-posta",
        DetectionType.Iban => "IBAN",
        DetectionType.FullName => "Ad Soyad",
        DetectionType.Address => "Adres",
        DetectionType.TesisatNo => "Tesisat No",
        DetectionType.AboneNo => "Abone No",
        DetectionType.SayacNo => "Sayaç No",
        DetectionType.MusteriNo => "Müşteri No",
        DetectionType.Date => "Tarih",
        DetectionType.TaxId => "Vergi No",
        DetectionType.PassportNo => "Pasaport No",
        DetectionType.CreditCard => "Kredi Kartı",
        DetectionType.PossiblePersonalData => "Olası Kişisel Veri",
        _ => type.ToString()
    };
}
