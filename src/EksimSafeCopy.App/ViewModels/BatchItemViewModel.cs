using EksimSafeCopy.Core.Models;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using EksimSafeCopy.Core.Abstractions;

namespace EksimSafeCopy.App.ViewModels;

public sealed class BatchItemViewModel : ViewModelBase
{
    private BatchItem _item;
    private bool _isSelected;

    public BatchItemViewModel(BatchItem item)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _isSelected = false;
    }

    public BatchItem Item => _item;

    public string Id => _item.Id;
    public string InputPath => _item.InputPath;
    public string FileName => _item.FileName;
    public DF DetectedFormat => _item.DetectedFormat;
    public BatchItemState State => _item.State;
    public string StatusMessage => _item.StatusMessage;
    public Error? Error => _item.Error;
    public string? OutputPath => _item.OutputPath;
    public VerificationResult? VerificationResult => _item.VerificationResult;
    public IReadOnlyList<Detection> Detections => _item.Detections;
    public string? OriginalHash => _item.OriginalHash;
    public string? OutputHash => _item.OutputHash;
    public DateTime CreatedAt => _item.CreatedAt;
    public TimeSpan Duration => _item.Duration;

    public int DetectionCount => _item.Detections?.Count ?? 0;
    public string DetectionSummary => DetectionCount == 0 ? "PII yok" : $"{DetectionCount} PII";
    public string VerificationSummary => _item.VerificationResult == null ? "-" : (_item.VerificationResult.Passed ? "Geçti" : $"Kaldı: {_item.VerificationResult.TotalResidualCount}");
    public string StateText => MapState(_item.State);
    public string StateColor => MapColor(_item.State);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void Update(BatchItem newItem)
    {
        _item = newItem;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(OutputPath));
        OnPropertyChanged(nameof(VerificationResult));
        OnPropertyChanged(nameof(Detections));
        OnPropertyChanged(nameof(DetectionCount));
        OnPropertyChanged(nameof(DetectionSummary));
        OnPropertyChanged(nameof(VerificationSummary));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DetectedFormat));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(OriginalHash));
        OnPropertyChanged(nameof(OutputHash));
    }

    private static string MapState(BatchItemState s) => s switch
    {
        BatchItemState.Queued => "Beklemede",
        BatchItemState.Processing => "İşleniyor",
        BatchItemState.Success => "Başarılı",
        BatchItemState.Failed => "Başarısız",
        BatchItemState.Unsupported => "Desteklenmiyor",
        BatchItemState.Cancelled => "İptal",
        BatchItemState.Skipped => "Atlandı",
        _ => s.ToString()
    };

    private static string MapColor(BatchItemState s) => s switch
    {
        BatchItemState.Success => "#2F8F4E",
        BatchItemState.Failed => "#B91C1C",
        BatchItemState.Unsupported => "#D97706",
        BatchItemState.Cancelled => "#64748B",
        BatchItemState.Processing => "#2563EB",
        BatchItemState.Queued => "#64748B",
        BatchItemState.Skipped => "#94A3B8",
        _ => "#334155"
    };
}
