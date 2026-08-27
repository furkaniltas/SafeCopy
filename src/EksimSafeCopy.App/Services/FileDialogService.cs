using Microsoft.Win32;

namespace EksimSafeCopy.App.Services;

public sealed class FileDialogService : IFileDialogService
{
    private const string DefaultFilter =
        "Desteklenen Dosyalar|*.pdf;*.docx;*.xlsx;*.txt;*.udf;*.png;*.jpg;*.jpeg;*.tiff;*.tif;*.bmp|" +
        "PDF Dosyaları (*.pdf)|*.pdf|" +
        "Word Belgeleri (*.docx)|*.docx|" +
        "Excel Çalışma Kitapları (*.xlsx)|*.xlsx|" +
        "Metin Dosyaları (*.txt)|*.txt|" +
        "UDF Dosyaları (*.udf)|*.udf|" +
        "Görüntü Dosyaları (*.png;*.jpg;*.jpeg;*.tiff;*.bmp)|*.png;*.jpg;*.jpeg;*.tiff;*.tif;*.bmp|" +
        "Tüm Dosyalar (*.*)|*.*";

    public string? OpenFile(string filter, string title = "Dosya Seç")
    {
        var dialog = new OpenFileDialog
        {
            Filter = string.IsNullOrWhiteSpace(filter) ? DefaultFilter : filter,
            Title = title,
            CheckFileExists = true,
            Multiselect = false
        };

        var result = dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string>? OpenFiles(string filter, string title = "Dosya Seç")
    {
        var dialog = new OpenFileDialog
        {
            Filter = string.IsNullOrWhiteSpace(filter) ? DefaultFilter : filter,
            Title = title,
            CheckFileExists = true,
            Multiselect = true
        };

        var result = dialog.ShowDialog();
        if (result == true && dialog.FileNames != null && dialog.FileNames.Length > 0)
            return dialog.FileNames.ToList().AsReadOnly();
        return null;
    }

    public string? SaveFile(string filter, string defaultFileName, string title = "Farklı Kaydet")
    {
        var dialog = new SaveFileDialog
        {
            Filter = string.IsNullOrWhiteSpace(filter) ? DefaultFilter : filter,
            FileName = defaultFileName,
            Title = title,
            OverwritePrompt = true
        };

        var result = dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public static string SupportedFilesFilter => DefaultFilter;
}
