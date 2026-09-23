namespace SafeCopy.App.Services;

public interface IFileDialogService
{
    string? OpenFile(string filter, string title = "Dosya Seç");
    string? SaveFile(string filter, string defaultFileName, string title = "Farklı Kaydet");
    IReadOnlyList<string>? OpenFiles(string filter, string title = "Dosya Seç");
}
