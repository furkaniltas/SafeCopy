using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SafeCopy.App.Services;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.DocumentEngine.Security;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;

namespace SafeCopy.App.ViewModels;

public enum ProcessingState
{
    Idle,
    Loading,
    Detecting,
    Ready,
    Redacting,
    Verifying,
    Success,
    Failed,
    Unsupported,
    Cancelled
}

public sealed class MainViewModel : ViewModelBase
{
    private readonly IDocumentEngine _documentEngine;
    private readonly IDetectionEngine _detectionEngine;
    private readonly IRenderer _renderer;
    private readonly IRedactionPlanner _redactionPlanner;
    private readonly IVerificationEngine _verificationEngine;
    private readonly IDocumentSecurityValidator _securityValidator;
    private readonly IFileSystem _fileSystem;
    private readonly IFileDialogService _fileDialogService;
    private readonly IBatchProcessor? _batchProcessor;

    private string? _selectedFilePath;
    private Document? _currentDocument;
    private DetectionItemViewModel? _selectedDetection;
    private string _previewText = string.Empty;
    private BitmapImage? _previewImage;
    private ProcessingState _processingState = ProcessingState.Idle;
    private string _statusMessage = "Dosya seçin ve taramayı başlatın.";
    private VerificationResult? _verificationResult;
    private string? _outputPath;
    private string? _originalHash;
    private string? _currentHash;
    private bool _isBusy;
    private CancellationTokenSource? _cts;
    private string? _unsupportedMessage;

    // Batch fields
    private bool _isBatchProcessing;
    private string _batchStatusMessage = "Toplu işlem hazır.";
    private BatchResult? _lastBatchResult;
    private CancellationTokenSource? _batchCts;
    private BatchItemViewModel? _selectedBatchItem;

    public MainViewModel(
        IDocumentEngine documentEngine,
        IDetectionEngine detectionEngine,
        IRenderer renderer,
        IRedactionPlanner redactionPlanner,
        IVerificationEngine verificationEngine,
        IDocumentSecurityValidator securityValidator,
        IFileSystem fileSystem,
        IFileDialogService fileDialogService,
        IBatchProcessor? batchProcessor = null)
    {
        _documentEngine = documentEngine ?? throw new ArgumentNullException(nameof(documentEngine));
        _detectionEngine = detectionEngine ?? throw new ArgumentNullException(nameof(detectionEngine));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _redactionPlanner = redactionPlanner ?? throw new ArgumentNullException(nameof(redactionPlanner));
        _verificationEngine = verificationEngine ?? throw new ArgumentNullException(nameof(verificationEngine));
        _securityValidator = securityValidator ?? throw new ArgumentNullException(nameof(securityValidator));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _batchProcessor = batchProcessor;

        Detections = new ObservableCollection<DetectionItemViewModel>();
        Detections.CollectionChanged += OnDetectionsCollectionChanged;
        BatchItems = new ObservableCollection<BatchItemViewModel>();
        BatchItems.CollectionChanged += OnBatchCollectionChanged;

        OpenFileCommand = new AsyncRelayCommand(_ => OpenFileAsync());
        DetectCommand = new AsyncRelayCommand(_ => DetectAsync(), _ => CanDetect);
        RedactCommand = new AsyncRelayCommand(_ => RedactAsync(), _ => CanRedact);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);
        NewScanCommand = new RelayCommand(_ => NewScan());
        OpenOutputCommand = new RelayCommand(_ => OpenOutput(), _ => !string.IsNullOrEmpty(OutputPath) && File.Exists(OutputPath));

        // Batch commands
        AddFilesToBatchCommand = new RelayCommand(_ => AddFilesToBatchViaDialog());
        // DragDrop uses AddFilesToBatch(IEnumerable<string>)
        StartBatchCommand = new AsyncRelayCommand(_ => StartBatchAsync(), _ => CanStartBatch);
        CancelBatchCommand = new RelayCommand(_ => CancelBatch(), _ => IsBatchProcessing);
        RetryFailedCommand = new AsyncRelayCommand(_ => RetryFailedAsync(), _ => CanRetryFailed);
        ClearBatchCommand = new RelayCommand(_ => ClearBatch(), _ => BatchItems.Count > 0 && !IsBatchProcessing);
        RemoveFromBatchCommand = new RelayCommand(param => { if (param is BatchItemViewModel vm) RemoveFromBatch(vm); }, _ => !IsBatchProcessing);
        OpenBatchOutputCommand = new RelayCommand(param => { if (param is BatchItemViewModel vm) OpenBatchOutput(vm); });
    }

    public ObservableCollection<DetectionItemViewModel> Detections { get; }
    public ObservableCollection<BatchItemViewModel> BatchItems { get; }

    public BatchResult? LastBatchResult
    {
        get => _lastBatchResult;
        set
        {
            if (SetProperty(ref _lastBatchResult, value))
            {
                OnPropertyChanged(nameof(BatchSuccessCount));
                OnPropertyChanged(nameof(BatchFailedCount));
                OnPropertyChanged(nameof(BatchUnsupportedCount));
                OnPropertyChanged(nameof(BatchCancelledCount));
                OnPropertyChanged(nameof(BatchTotalCount));
            }
        }
    }

    public bool IsBatchProcessing
    {
        get => _isBatchProcessing;
        set
        {
            if (SetProperty(ref _isBatchProcessing, value))
            {
                OnPropertyChanged(nameof(CanStartBatch));
                OnPropertyChanged(nameof(CanRetryFailed));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string BatchStatusMessage
    {
        get => _batchStatusMessage;
        set => SetProperty(ref _batchStatusMessage, value);
    }

    public int BatchSuccessCount => LastBatchResult?.SuccessCount ?? BatchItems.Count(x => x.State == BatchItemState.Success);
    public int BatchFailedCount => LastBatchResult?.FailedCount ?? BatchItems.Count(x => x.State == BatchItemState.Failed);
    public int BatchUnsupportedCount => LastBatchResult?.UnsupportedCount ?? BatchItems.Count(x => x.State == BatchItemState.Unsupported);
    public int BatchCancelledCount => LastBatchResult?.CancelledCount ?? BatchItems.Count(x => x.State == BatchItemState.Cancelled);
    public BatchItemViewModel? SelectedBatchItem
    {
        get => _selectedBatchItem;
        set
        {
            if (SetProperty(ref _selectedBatchItem, value))
            {
                if (value != null)
                {
                    // Sync Detail panel to selected batch item's result
                    VerificationResult = value.VerificationResult;
                    OutputPath = value.OutputPath;
                    StatusMessage = value.StatusMessage;
                }
            }
        }
    }

    public int BatchTotalCount => BatchItems.Count;

    public bool CanStartBatch => !IsBatchProcessing && !IsBusy && BatchItems.Any(x => x.State == BatchItemState.Queued || x.State == BatchItemState.Failed || x.State == BatchItemState.Cancelled || x.State == BatchItemState.Skipped);
    public bool CanRetryFailed => !IsBatchProcessing && !IsBusy && BatchItems.Any(x => x.State == BatchItemState.Failed);

    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set => SetProperty(ref _selectedFilePath, value);
    }

    public Document? CurrentDocument
    {
        get => _currentDocument;
        set
        {
            if (SetProperty(ref _currentDocument, value))
            {
                OnPropertyChanged(nameof(CanRedact));
            }
        }
    }

    public DetectionItemViewModel? SelectedDetection
    {
        get => _selectedDetection;
        set => SetProperty(ref _selectedDetection, value);
    }

    public string PreviewText
    {
        get => _previewText;
        set => SetProperty(ref _previewText, value);
    }

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        set => SetProperty(ref _previewImage, value);
    }

    public ProcessingState ProcessingState
    {
        get => _processingState;
        set
        {
            if (SetProperty(ref _processingState, value))
            {
                OnPropertyChanged(nameof(CanRedact));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public VerificationResult? VerificationResult
    {
        get => _verificationResult;
        set => SetProperty(ref _verificationResult, value);
    }

    public string? OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public string? OriginalHash
    {
        get => _originalHash;
        set => SetProperty(ref _originalHash, value);
    }

    public string? CurrentHash
    {
        get => _currentHash;
        set => SetProperty(ref _currentHash, value);
    }

    public string? UnsupportedMessage
    {
        get => _unsupportedMessage;
        set => SetProperty(ref _unsupportedMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRedact));
                OnPropertyChanged(nameof(CanStartBatch));
                OnPropertyChanged(nameof(CanRetryFailed));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanDetect => CurrentDocument != null && !IsBusy;

    public bool CanRedact
    {
        get
        {
            if (IsBusy) return false;
            if (CurrentDocument == null) return false;
            if (!Detections.Any(d => d.IsSelected)) return false;
            if (ProcessingState == ProcessingState.Unsupported) return false;
            return true;
        }
    }

    public bool HasDetections => Detections.Count > 0;

    public int SelectedCount => Detections.Count(d => d.IsSelected);

    public IEnumerable<DetectionItemViewModel> PossibleDetections => Detections.Where(d => d.Type == DetectionType.PossiblePersonalData);
    public bool HasPossibleDetections => PossibleDetections.Any();
    public int PossibleCount => PossibleDetections.Count();
    private bool _isPossiblePanelExpanded = false;
    public bool IsPossiblePanelExpanded
    {
        get => _isPossiblePanelExpanded;
        set => SetProperty(ref _isPossiblePanelExpanded, value);
    }
    public IEnumerable<DetectionItemViewModel> DefiniteDetections => Detections.Where(d => d.Type != DetectionType.PossiblePersonalData);
    public bool HasDefiniteDetections => DefiniteDetections.Any();
    public int DefiniteCount => DefiniteDetections.Count();
    public int SelectedDefiniteCount => DefiniteDetections.Count(d => d.IsSelected);

    private MaskingMode _selectedMaskingMode = MaskingMode.FullRedaction;
    public MaskingMode SelectedMaskingMode
    {
        get => _selectedMaskingMode;
        set => SetProperty(ref _selectedMaskingMode, value);
    }

    public Array AvailableMaskingModes => Enum.GetValues(typeof(MaskingMode));

    public ICommand OpenFileCommand { get; }
    public ICommand DetectCommand { get; }
    public ICommand RedactCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand NewScanCommand { get; }
    public ICommand OpenOutputCommand { get; }

    // Batch commands
    public ICommand AddFilesToBatchCommand { get; }
    public ICommand StartBatchCommand { get; }
    public ICommand CancelBatchCommand { get; }
    public ICommand RetryFailedCommand { get; }
    public ICommand ClearBatchCommand { get; }
    public ICommand RemoveFromBatchCommand { get; }
    public ICommand OpenBatchOutputCommand { get; }

    // Batch methods
    public void AddFilesToBatch(IEnumerable<string> paths)
    {
        if (paths == null) return;
        var existing = new HashSet<string>(BatchItems.Select(b => b.InputPath), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var trimmed = p.Trim();
            if (existing.Contains(trimmed)) continue;
            if (!File.Exists(trimmed))
            {
                var failed = new BatchItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    InputPath = trimmed,
                    FileName = Path.GetFileName(trimmed),
                    DetectedFormat = DF.Unknown,
                    State = BatchItemState.Failed,
                    StatusMessage = "Dosya bulunamadı",
                    Error = Error.NotFound($"File not found: {trimmed}"),
                    Detections = Array.Empty<Detection>(),
                    CreatedAt = DateTime.UtcNow
                };
                BatchItems.Add(new BatchItemViewModel(failed));
                continue;
            }
            existing.Add(trimmed);
            var item = new BatchItem
            {
                Id = Guid.NewGuid().ToString("N"),
                InputPath = trimmed,
                FileName = Path.GetFileName(trimmed),
                DetectedFormat = DF.Unknown,
                State = BatchItemState.Queued,
                StatusMessage = "Beklemede",
                Detections = Array.Empty<Detection>(),
                CreatedAt = DateTime.UtcNow
            };
            BatchItems.Add(new BatchItemViewModel(item));
            added++;
        }
        if (added > 0)
        {
            BatchStatusMessage = $"{added} dosya kuyruğa eklendi. Toplam: {BatchItems.Count}";
            OnPropertyChanged(nameof(BatchTotalCount));
            OnPropertyChanged(nameof(CanStartBatch));
        }
    }

    private void AddFilesToBatchViaDialog()
    {
        var filter = FileDialogService.SupportedFilesFilter;
        var files = _fileDialogService.OpenFiles(filter, "Dosyaları Seç - Toplu İşlem");
        if (files == null || files.Count == 0) return;
        AddFilesToBatch(files);
    }

    private async Task StartBatchAsync()
    {
        if (_batchProcessor == null)
        {
            BatchStatusMessage = "Batch processor unavailable.";
            return;
        }
        if (IsBatchProcessing || IsBusy) return;

        // Collect paths to process: Queued + Failed (for retry) + Cancelled/Skipped could be retried
        // But for initial start, process all Queued
        var toProcess = BatchItems.Where(x => x.State == BatchItemState.Queued).Select(x => x.InputPath).ToList();
        // If no queued but there are failed/cancelled and user clicked StartBatch, process queued only; retry uses dedicated command
        if (toProcess.Count == 0)
        {
            // If user has no queued, but wants to start with all not-success: fallback to queued+failed
            toProcess = BatchItems.Where(x => x.State != BatchItemState.Success && x.State != BatchItemState.Unsupported).Select(x => x.InputPath).ToList();
            if (toProcess.Count == 0)
            {
                BatchStatusMessage = "İşlenecek dosya yok.";
                return;
            }
        }

        var request = new BatchRequest
        {
            InputPaths = toProcess,
            Options = new RenderOptions
            {
                Mode = SelectedMaskingMode,
                UseTypePlaceholder = SelectedMaskingMode == MaskingMode.FullRedaction,
                SanitizeMetadata = true,
                RemoveHiddenContent = true
            },
            ContinueOnError = true,
            MaxDegreeOfParallelism = 1
        };

        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;

        try
        {
            IsBatchProcessing = true;
            IsBusy = true;
            BatchStatusMessage = $"Toplu işlem başlıyor ({toProcess.Count} dosya)...";
            LastBatchResult = null;

            // Progress reporter - ALWAYS marshal via Dispatcher
            var progress = new Progress<BatchItem>(item =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                Action update = () =>
                {
                    var vm = BatchItems.FirstOrDefault(x => string.Equals(x.InputPath, item.InputPath, StringComparison.OrdinalIgnoreCase));
                    if (vm != null)
                    {
                        vm.Update(item);
                    }
                    else
                    {
                        // Should not happen, but add if missing
                        BatchItems.Add(new BatchItemViewModel(item));
                    }
                    BatchStatusMessage = $"{item.FileName}: {item.StatusMessage} ({item.State})";
                };
                dispatcher.BeginInvoke(update);
            });

            var result = await _batchProcessor.ProcessAsync(request, progress, token).ConfigureAwait(false);

            if (result.IsFailure)
            {
                BatchStatusMessage = $"Toplu işlem hatası: {result.Error.Message}";
                await RunOnUiAsync(() => LastBatchResult = null).ConfigureAwait(false);
                return;
            }

            var batchResult = result.Value;
            await RunOnUiAsync(() =>
            {
                // Update all items to final state
                foreach (var bi in batchResult.Items)
                {
                    var vm = BatchItems.FirstOrDefault(x => string.Equals(x.InputPath, bi.InputPath, StringComparison.OrdinalIgnoreCase));
                    if (vm != null) vm.Update(bi);
                    else BatchItems.Add(new BatchItemViewModel(bi));
                }
                LastBatchResult = batchResult;
                OnPropertyChanged(nameof(BatchSuccessCount));
                OnPropertyChanged(nameof(BatchFailedCount));
                OnPropertyChanged(nameof(BatchUnsupportedCount));
                OnPropertyChanged(nameof(BatchCancelledCount));
                OnPropertyChanged(nameof(BatchTotalCount));
                OnPropertyChanged(nameof(CanRetryFailed));
            }).ConfigureAwait(false);

            // After batch completes, load the first successful item's ORIGINAL file for preview
            // Run entire LoadAndDetectAsync on UI thread so all internal RunOnUiAsync calls execute inline
            var firstSuccess = batchResult.Items.FirstOrDefault(x => x.State == BatchItemState.Success);
            if (firstSuccess != null && !string.IsNullOrWhiteSpace(firstSuccess.InputPath) && File.Exists(firstSuccess.InputPath))
            {
                await RunOnUiAsync(async () =>
                {
                    await LoadAndDetectAsync(firstSuccess.InputPath);
                    SelectedFilePath = firstSuccess.InputPath;
                    var vm = BatchItems.FirstOrDefault(x => string.Equals(x.InputPath, firstSuccess.InputPath, StringComparison.OrdinalIgnoreCase));
                    if (vm != null)
                    {
                        SelectedBatchItem = vm;
                        // Override the single-file VerificationResult (null) with the batch's actual verification
                        VerificationResult = firstSuccess.VerificationResult;
                        OutputPath = firstSuccess.OutputPath;
                        StatusMessage = vm.StatusMessage;
                    }
                }).ConfigureAwait(false);
            }

            await RunOnUiAsync(() =>
            {
                BatchStatusMessage = batchResult.IsCancelled
                    ? $"İptal edildi. Başarılı: {batchResult.SuccessCount}, Başarısız: {batchResult.FailedCount}, Desteklenmiyor: {batchResult.UnsupportedCount}"
                    : $"Tamamlandı. Başarılı: {batchResult.SuccessCount}, Başarısız: {batchResult.FailedCount}, Desteklenmiyor: {batchResult.UnsupportedCount}";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            BatchStatusMessage = "Toplu işlem iptal edildi.";
        }
        catch (Exception ex)
        {
            BatchStatusMessage = $"Toplu işlem hatası: {ex.Message}";
        }
        finally
        {
            IsBatchProcessing = false;
            IsBusy = false;
            OnPropertyChanged(nameof(CanStartBatch));
            OnPropertyChanged(nameof(CanRetryFailed));
        }
    }

    private async Task RetryFailedAsync()
    {
        if (_batchProcessor == null) return;
        if (IsBatchProcessing) return;

        var failedPaths = BatchItems.Where(x => x.State == BatchItemState.Failed).Select(x => x.InputPath).ToList();
        if (failedPaths.Count == 0)
        {
            BatchStatusMessage = "Yeniden denenecek başarısız dosya yok.";
            return;
        }

        // Reset failed items to Queued before retry
        foreach (var vm in BatchItems.Where(x => x.State == BatchItemState.Failed).ToList())
        {
            var queued = new BatchItem
            {
                Id = vm.Id,
                InputPath = vm.InputPath,
                FileName = vm.FileName,
                DetectedFormat = vm.DetectedFormat,
                State = BatchItemState.Queued,
                StatusMessage = "Yeniden denenecek",
                Detections = Array.Empty<Detection>(),
                CreatedAt = DateTime.UtcNow
            };
            vm.Update(queued);
        }

        await StartBatchAsync().ConfigureAwait(false);
    }

    private void CancelBatch()
    {
        try
        {
            _batchCts?.Cancel();
            BatchStatusMessage = "İptal ediliyor...";
        }
        catch { }
    }

    private void ClearBatch()
    {
        if (IsBatchProcessing) return;
        BatchItems.Clear();
        LastBatchResult = null;
        BatchStatusMessage = "Kuyruk temizlendi.";
        OnPropertyChanged(nameof(BatchTotalCount));
        OnPropertyChanged(nameof(BatchSuccessCount));
        OnPropertyChanged(nameof(BatchFailedCount));
        OnPropertyChanged(nameof(BatchUnsupportedCount));
        OnPropertyChanged(nameof(BatchCancelledCount));
    }

    private void RemoveFromBatch(BatchItemViewModel vm)
    {
        if (IsBatchProcessing) return;
        if (vm.State == BatchItemState.Processing)
        {
            BatchStatusMessage = "İşlenen dosya kaldırılamaz.";
            return;
        }
        BatchItems.Remove(vm);
        OnPropertyChanged(nameof(BatchTotalCount));
        OnPropertyChanged(nameof(BatchSuccessCount));
        OnPropertyChanged(nameof(BatchFailedCount));
        OnPropertyChanged(nameof(CanStartBatch));
        BatchStatusMessage = $"{vm.FileName} kuyruktan çıkarıldı.";
    }

    private void OpenBatchOutput(BatchItemViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.OutputPath) || !File.Exists(vm.OutputPath))
        {
            BatchStatusMessage = "Çıktı bulunamadı: " + vm.FileName;
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(vm.OutputPath) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            BatchStatusMessage = $"Açılamadı: {ex.Message}";
        }
    }

    private void OnBatchCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(BatchTotalCount));
        OnPropertyChanged(nameof(BatchSuccessCount));
        OnPropertyChanged(nameof(BatchFailedCount));
        OnPropertyChanged(nameof(BatchUnsupportedCount));
        OnPropertyChanged(nameof(BatchCancelledCount));
        OnPropertyChanged(nameof(CanStartBatch));
        OnPropertyChanged(nameof(CanRetryFailed));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void OnDetectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseDetectionCountProperties();
    }

    private void RaiseDetectionCountProperties()
    {
        OnPropertyChanged(nameof(HasDetections));
        OnPropertyChanged(nameof(DefiniteCount));
        OnPropertyChanged(nameof(DefiniteDetections));
        OnPropertyChanged(nameof(HasDefiniteDetections));
        OnPropertyChanged(nameof(PossibleCount));
        OnPropertyChanged(nameof(PossibleDetections));
        OnPropertyChanged(nameof(HasPossibleDetections));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedDefiniteCount));
        OnPropertyChanged(nameof(CanRedact));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task OpenFileAsync()
    {
        var filter = FileDialogService.SupportedFilesFilter;
        var path = _fileDialogService.OpenFile(filter, "Dosya Seç - SafeCopy");
        if (string.IsNullOrWhiteSpace(path))
            return;

        await RunOnUiAsync(() => SelectedFilePath = path).ConfigureAwait(false);
        await LoadAndDetectAsync(path).ConfigureAwait(false);
    }

    private async Task DetectAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath) || CurrentDocument == null)
        {
            await RunOnUiAsync(() => StatusMessage = "Önce bir dosya seçin.").ConfigureAwait(false);
            return;
        }

        await RunDetectionAsync(CurrentDocument).ConfigureAwait(false);
    }

    public async Task LoadAndDetectAsync(string filePath)
    {
        CancelPrevious();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            // Reset UI state on UI thread
            await RunOnUiAsync(() =>
            {
                IsBusy = true;
                ProcessingState = ProcessingState.Loading;
                StatusMessage = "Dosya doğrulanıyor...";
                UnsupportedMessage = null;
                VerificationResult = null;
                OutputPath = null;
                Detections.Clear();
                PreviewText = string.Empty;
                PreviewImage = null;
                OriginalHash = null;
                CurrentHash = null;
            }).ConfigureAwait(false);

            // Format validation + security validation executed on background thread via DocumentEngine internally
            // Compute original hash before ingestion for immutability proof
            var hashResult = await Task.Run(() => _fileSystem.ComputeHash(filePath, HashAlgorithm.SHA256), token).ConfigureAwait(false);
            if (hashResult.IsSuccess)
            {
                await RunOnUiAsync(() =>
                {
                    OriginalHash = hashResult.Value;
                    StatusMessage = $"Orijinal dosya hash (SHA256): {OriginalHash[..16]}...";
                }).ConfigureAwait(false);
            }
            else
            {
                // Fallback to security validator
                var altHash = await Task.Run(() => _securityValidator.ComputeFileHash(filePath, HashAlgorithm.SHA256), token).ConfigureAwait(false);
                if (altHash.IsSuccess)
                    OriginalHash = altHash.Value;
            }

            // Ingestion via Common Document Model - async with cancellation
            var ingestionResult = await Task.Run(() => _documentEngine.Load(filePath, token), token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                await RunOnUiAsync(() =>
                {
                    ProcessingState = ProcessingState.Cancelled;
                    StatusMessage = "İşlem iptal edildi.";
                }).ConfigureAwait(false);
                return;
            }

            if (ingestionResult.IsFailure)
            {
                await RunOnUiAsync(() =>
                {
                    ProcessingState = ProcessingState.Failed;
                    StatusMessage = $"Dosya yüklenemedi: {ingestionResult.Error.Message}";
                }).ConfigureAwait(false);
                return;
            }

            var document = ingestionResult.Value;
            // Ensure UI sees document from model, not raw file
            await RunOnUiAsync(() => CurrentDocument = document).ConfigureAwait(false);

            // Verify original file immutability after ingestion
            var postHashResult = await Task.Run(() => _fileSystem.ComputeHash(filePath, HashAlgorithm.SHA256), token).ConfigureAwait(false);
            if (postHashResult.IsSuccess && OriginalHash != null && postHashResult.Value != OriginalHash)
            {
                await RunOnUiAsync(() =>
                {
                    ProcessingState = ProcessingState.Failed;
                    StatusMessage = "GÜVENLİK HATASI: Orijinal dosya değiştirildi!";
                }).ConfigureAwait(false);
                return;
            }
            await RunOnUiAsync(() =>
            {
                CurrentHash = postHashResult.IsSuccess ? postHashResult.Value : OriginalHash;
            }).ConfigureAwait(false);

            // Build preview from Document model (not direct file)
            await BuildPreviewAsync(document, token).ConfigureAwait(false);

            // Load image preview if applicable
            TryLoadImagePreview(document);

            // Detection phase
            await RunDetectionAsync(document).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RunOnUiAsync(() =>
            {
                ProcessingState = ProcessingState.Cancelled;
                StatusMessage = "İşlem iptal edildi.";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                ProcessingState = ProcessingState.Failed;
                StatusMessage = $"Beklenmeyen hata: {ex.Message}";
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private async Task RunDetectionAsync(Document document)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            await RunOnUiAsync(() =>
            {
                IsBusy = true;
                ProcessingState = ProcessingState.Detecting;
                StatusMessage = "PII taraması yapılıyor...";
            }).ConfigureAwait(false);

            var detectResult = await Task.Run(() => _detectionEngine.Detect(document, token), token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                await RunOnUiAsync(() =>
                {
                    ProcessingState = ProcessingState.Cancelled;
                    StatusMessage = "Tarama iptal edildi.";
                }).ConfigureAwait(false);
                return;
            }

            if (detectResult.IsFailure)
            {
                await RunOnUiAsync(() =>
                {
                    ProcessingState = ProcessingState.Failed;
                    StatusMessage = $"Tarama başarısız: {detectResult.Error.Message}";
                }).ConfigureAwait(false);
                return;
            }

            var detections = detectResult.Value;

            await RunOnUiAsync(() =>
            {
                Detections.Clear();
                foreach (var d in detections)
                {
                    var isSelected = d.Type != DetectionType.PossiblePersonalData;
                    var vm = new DetectionItemViewModel(d, isSelected: isSelected);
                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(DetectionItemViewModel.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedCount));
                            OnPropertyChanged(nameof(SelectedDefiniteCount));
                            OnPropertyChanged(nameof(CanRedact));
                            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                        }
                    };
                    Detections.Add(vm);
                }
                RaiseDetectionCountProperties();
            }).ConfigureAwait(false);

            // Update preview with bbox markers using CoordinateSystem
            await UpdatePreviewWithMarkersAsync(document, detections, token).ConfigureAwait(false);

            // Handle unsupported formats already flagged for redaction (UDF now supported via UdfRedactor FAZ 8.1)
            if (document.Format == DF.Pdf)
            {
                if (detections.Any())
                {
                    await RunOnUiAsync(() =>
                    {
                        ProcessingState = ProcessingState.Unsupported;
                        UnsupportedMessage = "PDF redaction şu anda güvenli olarak desteklenmiyor.";
                        StatusMessage = UnsupportedMessage + " Dosya güvenli şekilde maskelenemedi, çıktı oluşturulmadı.";
                    }).ConfigureAwait(false);
                    return;
                }
            }

            await RunOnUiAsync(() =>
            {
                ProcessingState = ProcessingState.Ready;
                StatusMessage = detections.Count == 0
                    ? "PII bulunamadı. Maskeleme gereksiz."
                    : $"{detections.Count} adet PII bulundu. Maskelenecek öğeleri seçin ve 'Maskele' butonuna basın.";

                // Set hash display after successful detection
                if (OriginalHash != null)
                    StatusMessage += $" | SHA256: {OriginalHash[..12]}... doğrulanmış.";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RunOnUiAsync(() =>
            {
                ProcessingState = ProcessingState.Cancelled;
                StatusMessage = "Tarama iptal edildi.";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                ProcessingState = ProcessingState.Failed;
                StatusMessage = $"Tarama hatası: {ex.Message}";
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private async Task RedactAsync()
    {
        if (CurrentDocument == null || string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            StatusMessage = "Dosya ve tespitler hazır değil.";
            return;
        }

        var selectedDetections = Detections.Where(d => d.IsSelected).Select(d => d.Detection).ToList();
        if (selectedDetections.Count == 0)
        {
            StatusMessage = "Maskelenecek PII seçilmedi.";
            return;
        }

        // Unsupported format check - mandatory security rule (PDF only, UDF now supported FAZ 8.1)
        if (CurrentDocument.Format == DF.Pdf)
        {
            ProcessingState = ProcessingState.Unsupported;
            var msg = "PDF redaction şu anda güvenli olarak desteklenmiyor.";
            UnsupportedMessage = msg;
            StatusMessage = msg + " Güvenli çıktı oluşturulmadı.";
            VerificationResult = null;
            // Do not overlay and claim success - explicit failure path
            return;
        }

        CancelPrevious();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            IsBusy = true;
            ProcessingState = ProcessingState.Redacting;
            StatusMessage = "Maskeleme uygulanıyor...";
            VerificationResult = null;

            // Immutability check before redaction
            var preHash = await Task.Run(() => _fileSystem.ComputeHash(SelectedFilePath!, HashAlgorithm.SHA256), token).ConfigureAwait(false);
            if (preHash.IsSuccess && OriginalHash != null && preHash.Value != OriginalHash)
            {
                StatusMessage = "GÜVENLİK HATASI: Orijinal dosya işlem sırasında değiştirildi!";
                ProcessingState = ProcessingState.Failed;
                return;
            }

            var options = new RenderOptions
            {
                Mode = SelectedMaskingMode,
                UseTypePlaceholder = SelectedMaskingMode == MaskingMode.FullRedaction,
                SanitizeMetadata = true,
                RemoveHiddenContent = true
            };

            // Resolve output path - preserve .udf.zip double extension
            var dir = Path.GetDirectoryName(SelectedFilePath)!;
            string outputFileName;
            if (SelectedFilePath.EndsWith(".udf.zip", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = Path.GetFileName(SelectedFilePath)[..^".udf.zip".Length];
                outputFileName = $"{baseName}_SafeCopy.udf.zip";
            }
            else
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(SelectedFilePath);
                var ext = Path.GetExtension(SelectedFilePath);
                outputFileName = $"{nameWithoutExt}_SafeCopy{ext}";
            }
            var outputPath = Path.Combine(dir, outputFileName);

            // Optional: ask user for save location? For automated flow use auto path.
            // We could use SaveFile dialog but spec says use explicit Redact button without overlay claim.
            // Keep auto-generated path.

            // Create plan then render via IRenderer
            var planResult = await Task.Run(() => _redactionPlanner.CreatePlan(CurrentDocument!, selectedDetections, options, token), token).ConfigureAwait(false);
            if (planResult.IsFailure)
            {
                ProcessingState = ProcessingState.Failed;
                StatusMessage = $"Maskeleme planı oluşturulamadı: {planResult.Error.Message}";
                return;
            }

            // Render to file using IRenderer (internally uses redactors)
            var renderResult = await Task.Run(() => _renderer.RenderToFile(CurrentDocument!, selectedDetections, options, outputPath, token), token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                ProcessingState = ProcessingState.Cancelled;
                StatusMessage = "Maskeleme iptal edildi.";
                return;
            }

            if (renderResult.IsFailure)
            {
                ProcessingState = ProcessingState.Failed;
                // Check if security error for unsupported format
                if (renderResult.Error.Code == "SECURITY_ERROR")
                {
                    UnsupportedMessage = "PDF redaction şu anda güvenli olarak desteklenmiyor.";
                    ProcessingState = ProcessingState.Unsupported;
                }
                StatusMessage = $"Maskeleme başarısız: {renderResult.Error.Message}";
                // Ensure no output file remains on failure
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }
                return;
            }

            OutputPath = outputPath;

            // Verify original immutability after render
            var postHash = await Task.Run(() => _fileSystem.ComputeHash(SelectedFilePath!, HashAlgorithm.SHA256), token).ConfigureAwait(false);
            if (postHash.IsSuccess && OriginalHash != null && postHash.Value != OriginalHash)
            {
                StatusMessage = "GÜVENLİK HATASI: Orijinal dosya maskeleme sonrası değiştirildi!";
                ProcessingState = ProcessingState.Failed;
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                    OutputPath = null;
                }
                return;
            }

            // Verification display - mandatory Output → Re-scan → PII detection → Residual PII?
            ProcessingState = ProcessingState.Verifying;
            StatusMessage = "Doğrulama yapılıyor (çıktı tekrar taranıyor)...";

            var verifyFormatResult = CurrentDocument.Format;
            var verifyResult = await Task.Run(() => _verificationEngine.Verify(outputPath, verifyFormatResult, selectedDetections, options, token), token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                ProcessingState = ProcessingState.Cancelled;
                StatusMessage = "Doğrulama iptal edildi.";
                return;
            }

            if (verifyResult.IsFailure)
            {
                ProcessingState = ProcessingState.Failed;
                StatusMessage = $"Doğrulama başarısız: {verifyResult.Error.Message}";
                VerificationResult = null;
                return;
            }

            var verification = verifyResult.Value;
            // Filter residual detections: deselected PII is allowed to remain, not counted as residual
            var deselectedValues = Detections.Where(d => !d.IsSelected).Select(d => d.Detection.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (deselectedValues.Count > 0 && verification.ResidualDetections.Count > 0)
            {
                var filtered = verification.ResidualDetections.Where(r => !deselectedValues.Contains(r.Value)).ToList();
                if (filtered.Count != verification.ResidualDetections.Count)
                {
                    verification = new VerificationResult
                    {
                        Passed = filtered.Count == 0 && verification.MetadataIssues.Count == 0 && verification.HiddenContentIssues.Count == 0,
                        ResidualDetections = filtered,
                        MetadataIssues = verification.MetadataIssues,
                        HiddenContentIssues = verification.HiddenContentIssues,
                        ScanDuration = verification.ScanDuration,
                        VerifiedAt = verification.VerifiedAt
                    };
                }
            }
            await RunOnUiAsync(() => VerificationResult = verification).ConfigureAwait(false);

            // Enforce 10 SUCCESS invariants before claiming success
            bool outputExists = File.Exists(outputPath);
            bool outputDifferent = !string.Equals(outputPath, SelectedFilePath, StringComparison.OrdinalIgnoreCase) && outputExists;
            var finalHashCheck = await Task.Run(() => _fileSystem.ComputeHash(SelectedFilePath!, HashAlgorithm.SHA256), token).ConfigureAwait(false);
            bool hashPreserved = finalHashCheck.IsSuccess && OriginalHash != null && finalHashCheck.Value == OriginalHash;
            bool passed = verification.Passed;
            bool zeroResidual = verification.TotalResidualCount == 0;
            bool zeroCritical = verification.CriticalResidualCount == 0;
            bool noMetadata = verification.MetadataIssues.Count == 0;
            bool noHidden = verification.HiddenContentIssues.Count == 0;

            bool allInvariants = outputExists && outputDifferent && hashPreserved && passed && zeroResidual && zeroCritical && noMetadata && noHidden;

            if (!allInvariants)
            {
                ProcessingState = ProcessingState.Failed;
                StatusMessage = $"GÜVENLİK DOĞRULAMASI BAŞARISIZ: residual={verification.TotalResidualCount}, critical={verification.CriticalResidualCount}, metadata={verification.MetadataIssues.Count}, hidden={verification.HiddenContentIssues.Count}, outputExists={outputExists}, hashPreserved={hashPreserved}. Güvenli kopya hazır değil.";
                // Clean up insecure output
                try { if (File.Exists(outputPath)) File.Delete(outputPath); OutputPath = null; } catch { }
                return;
            }

            // Only now claim success
            ProcessingState = ProcessingState.Success;
            StatusMessage = $"Güvenli kopya hazır: {Path.GetFileName(outputPath)} | Doğrulama geçti | SHA256 korundu.";

            // Keep original document reference and preview (original file unchanged)
            // Output file is separate - user can open it via OpenOutputCommand
        }
        catch (OperationCanceledException)
        {
            ProcessingState = ProcessingState.Cancelled;
            StatusMessage = "İşlem iptal edildi.";
        }
        catch (Exception ex)
        {
            ProcessingState = ProcessingState.Failed;
            StatusMessage = $"Maskeleme hatası: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BuildPreviewAsync(Document document, CancellationToken token)
    {
        await Task.Run(async () =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Belge: {document.Name}");
            sb.AppendLine($"Format: {document.Format}");
            sb.AppendLine($"Sayfa sayısı: {document.PageCount}");
            sb.AppendLine(new string('-', 40));

            foreach (var page in document.Pages)
            {
                token.ThrowIfCancellationRequested();
                sb.AppendLine($"[Sayfa {page.PageNumber + 1} - {page.Width}x{page.Height} DPI {page.DpiX}x{page.DpiY}]");
                if (!string.IsNullOrWhiteSpace(page.Text))
                {
                    // Limit preview length
                    var text = page.Text.Length > 2000 ? page.Text[..2000] + "..." : page.Text;
                    sb.AppendLine(text);
                }
                else if (page.TextBlocks.Count > 0)
                {
                    foreach (var block in page.TextBlocks.Take(20))
                    {
                        // Use CoordinateSystem if available
                        var bboxInfo = block.BoundingBox.IsEmpty ? "" : $" [bbox {block.BoundingBox.X:F0},{block.BoundingBox.Y:F0} {block.BoundingBox.Width:F0}x{block.BoundingBox.Height:F0}]";
                        if (page.CoordinateSystem != null)
                        {
                            var normalized = page.CoordinateSystem.Normalize(block.BoundingBox);
                            bboxInfo += $" norm({normalized.X:F2},{normalized.Y:F2})";
                        }
                        sb.AppendLine($"{block.Text}{bboxInfo}");
                    }
                }
                else if (page.Images.Count > 0)
                {
                    sb.AppendLine($"[Görüntü sayfası - {page.Images.Count} görüntü]");
                }
                else
                {
                    sb.AppendLine("[Boş sayfa]");
                }
                sb.AppendLine();
            }

            var preview = sb.ToString();
            await RunOnUiAsync(() => PreviewText = preview).ConfigureAwait(false);

            // Image preview for image documents via Document model images (not direct file)
            if (document.Format is DF.Png or DF.Jpeg or DF.Tiff or DF.Bmp)
            {
                TryLoadImagePreview(document);
            }
        }, token).ConfigureAwait(false);
    }

    private async Task UpdatePreviewWithMarkersAsync(Document document, IReadOnlyList<Detection> detections, CancellationToken token)
    {
        await Task.Run(async () =>
        {
            if (detections.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(PreviewText);
            sb.AppendLine(new string('=', 40));
            sb.AppendLine("Tespit edilen PII ve koordinatları (CoordinateSystem ile):");
            foreach (var d in detections)
            {
                token.ThrowIfCancellationRequested();
                var bbox = d.Location;
                var coordInfo = string.Empty;
                // Use page CoordinateSystem if available to demonstrate normalized coordinates
                var page = document.Pages.FirstOrDefault(p => p.PageNumber == d.PageNumber);
                if (page?.CoordinateSystem != null && !bbox.IsEmpty)
                {
                    var normalized = page.CoordinateSystem.Normalize(bbox);
                    coordInfo = $" | normalized: [{normalized.X:F3}, {normalized.Y:F3}, {normalized.Width:F3}, {normalized.Height:F3}]";
                }
                else if (!bbox.IsEmpty)
                {
                    coordInfo = $" | bbox: [{bbox.X:F1}, {bbox.Y:F1}, {bbox.Width:F1}, {bbox.Height:F1}]";
                }
                sb.AppendLine($"- {d.Type} '{d.Value}' sayfa {d.PageNumber + 1} güven {MapConfidence(d.ConfidenceLevel)}{coordInfo}");
            }

            var final = sb.ToString();
            await RunOnUiAsync(() => PreviewText = final).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    private void TryLoadImagePreview(Document document)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(document.Source.FilePath) || !File.Exists(document.Source.FilePath))
                return;

            Action load = () =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(document.Source.FilePath);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage = bitmap;
                }
                catch
                {
                    PreviewImage = null;
                }
            };

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(load);
            else
                load();
        }
        catch
        {
            // Silent - preview remains text only
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

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
            StatusMessage = "İptal ediliyor...";
        }
        catch { }
    }

    private void CancelPrevious()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    private void NewScan()
    {
        CancelPrevious();
        SelectedFilePath = null;
        CurrentDocument = null;
        Detections.Clear();
        PreviewText = string.Empty;
        PreviewImage = null;
        VerificationResult = null;
        OutputPath = null;
        OriginalHash = null;
        CurrentHash = null;
        UnsupportedMessage = null;
        SelectedDetection = null;
        ProcessingState = ProcessingState.Idle;
        StatusMessage = "Yeni tarama için dosya seçin.";
        RaiseDetectionCountProperties();
    }

    private void OpenOutput()
    {
        if (string.IsNullOrWhiteSpace(OutputPath) || !File.Exists(OutputPath))
        {
            StatusMessage = "Çıktı dosyası bulunamadı.";
            return;
        }

        try
        {
            var psi = new ProcessStartInfo(OutputPath) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dosya açılamadı: {ex.Message}";
        }
    }

    private static async Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
        }
        else
        {
            action();
        }
    }

    private static async Task RunOnUiAsync(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(action).Task.Unwrap().ConfigureAwait(false);
        }
        else
        {
            await action();
        }
    }
}
