using System.Globalization;
using System.Windows.Data;
using SafeCopy.Core.Models;

namespace SafeCopy.App.Converters;

public sealed class DetectionTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DetectionType type)
        {
            return type switch
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
                DetectionType.MusteriNo => "Müsteri No",
                DetectionType.Date => "Tarih",
                DetectionType.TaxId => "Vergi No",
                DetectionType.PassportNo => "Pasaport No",
                DetectionType.CreditCard => "Kredi Kartı",
                DetectionType.PossiblePersonalData => "Olası Kişisel Veri",
                _ => type.ToString()
            };
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ConfidenceLevelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConfidenceLevel level)
        {
            return level switch
            {
                ConfidenceLevel.Low => "Low",
                ConfidenceLevel.Medium => "Medium",
                ConfidenceLevel.High => "High",
                ConfidenceLevel.Critical => "High",
                _ => "Medium"
            };
        }

        if (value is double d)
        {
            if (d >= 0.85) return "High";
            if (d >= 0.6) return "Medium";
            return "Low";
        }

        return "Medium";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter as string == "Invert";
        var visible = value is bool b && b;
        if (invert) visible = !visible;
        return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class MaskingModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MaskingMode mode)
        {
            return mode switch
            {
                MaskingMode.FullRedaction => "Tam Maskeleme",
                MaskingMode.PartialMask => "Kısmi Maskeleme",
                MaskingMode.Placeholder => "Yer Tutucu",
                MaskingMode.Custom => "Özel",
                _ => mode.ToString()
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            return s switch
            {
                "Tam Maskeleme" => MaskingMode.FullRedaction,
                "Kısmi Maskeleme" => MaskingMode.PartialMask,
                _ => MaskingMode.FullRedaction
            };
        }
        if (value is MaskingMode m) return m;
        return MaskingMode.FullRedaction;
    }
}
