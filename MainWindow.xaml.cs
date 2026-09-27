using System.Windows;
using WindowCentering.ViewModels;

namespace WindowCentering;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
