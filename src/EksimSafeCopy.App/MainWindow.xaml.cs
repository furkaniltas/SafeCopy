using System.Windows;
using EksimSafeCopy.App.ViewModels;

namespace EksimSafeCopy.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
