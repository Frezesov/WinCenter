using System.Windows;
using WinCenter.ViewModels;

namespace WinCenter;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
