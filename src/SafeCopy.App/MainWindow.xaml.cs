using System.Windows;
using SafeCopy.App.ViewModels;

namespace SafeCopy.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        Drop += OnDrop;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                // If multiple files -> batch queue
                if (files.Length > 1)
                {
                    _viewModel.AddFilesToBatch(files);
                }
                else
                {
                    // Single file: decide if batch mode has items -> add to batch else single-file flow
                    // For usability, single drop to batch if batch already has items, else load single
                    if (_viewModel.BatchItems.Count > 0)
                        _viewModel.AddFilesToBatch(files);
                    else
                        _ = _viewModel.LoadAndDetectAsync(files[0]);
                }
            }
        }
        e.Handled = true;
    }
}
