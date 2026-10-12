using System.Windows;
using System.Windows.Threading;
using KoaZa.Foreseer.ViewModels;

namespace KoaZa.Foreseer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(20) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            _refreshTimer.Tick += async (_, _) => await _viewModel.RefreshAsync();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }
}
