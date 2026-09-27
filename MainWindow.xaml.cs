using System.Windows;
using System.Windows.Controls;
using WinCenter.Controls;
using WinCenter.ViewModels;

namespace WinCenter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _vm = viewModel;
    }

    // Programs with open windows are offered first, like the "Add an app" pickers in Windows Settings.
    private void OnAddExclusion(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(StyleProperty, "MenuStyle");

        var programs = _vm.GetRunningPrograms();
        foreach (var program in programs)
        {
            var item = new MenuItem { Header = program.Name, InputGestureText = program.Exe };
            if (ProgramIcon.Load(program.Path) is { } icon)
                item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
            item.Click += (_, _) => _vm.AddExclusion(program.Path);
            menu.Items.Add(item);
        }
        if (programs.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Нет открытых программ", IsEnabled = false });

        menu.Items.Add(new Separator());
        var browse = new MenuItem { Header = "Выбрать файл…" };
        browse.Click += (_, _) => BrowseForProgram();
        menu.Items.Add(browse);

        MenuPlacement.OpenBelow(menu, AddExclusionButton);
    }

    private void BrowseForProgram()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Программа, которую не центрировать автоматически",
            Filter = "Программы (*.exe)|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) == true)
            _vm.AddExclusion(dialog.FileName);
    }
}
