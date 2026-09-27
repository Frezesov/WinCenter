using System.Windows.Media;
using WinCenter.Core;

namespace WinCenter.ViewModels;

// A class, not a record: list items must keep reference equality.
public sealed class ExcludedAppViewModel
{
    internal ExcludedAppViewModel(ExcludedApp app, Action<ExcludedAppViewModel> remove)
    {
        App = app;
        Name = app.Path is { } path && System.IO.File.Exists(path) ? WindowInfo.GetProgramName(path) : app.Exe;
        Icon = ProgramIcon.Load(app.Path);
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    internal ExcludedApp App { get; }

    public string Name { get; }

    public string Exe => App.Exe;

    public ImageSource? Icon { get; }

    public RelayCommand RemoveCommand { get; }

    public override string ToString() => Name;
}

/// <summary>A program with an open window, offered when adding an exclusion.</summary>
internal sealed record RunningProgram(string Path, string Exe, string Name);
