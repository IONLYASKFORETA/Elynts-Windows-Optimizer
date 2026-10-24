using System.Diagnostics;
using System.Windows;
using Elynt.App.Theme;
using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Drivers;
using Elynt.Core.Startup;

namespace Elynt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private TrayNotes? _tray;

    public MainWindow()
    {
        _model = new MainViewModel(ConfirmDestructive, AskToReboot, PickGame, ConfirmWarning);
        _model.AppsManager = new AppsManagerViewModel(_model, ConfirmNotReviewed, ConfirmUninstall);
        _model.ServicesPage = new ServicesPageViewModel(() => _model.Client, ConfirmDriverChange);
        _model.Cleanup.ConfirmDestructive = ConfirmDestructiveCleanup;
        _model.Drift = new DriftViewModel(() => _model.Client, ConfirmDestructiveReapply, _model.RefreshAsync);
        InitializeComponent();
        // Opens at 1600×1000, or the screen's work area where that is smaller, and never remembers its size.
        var screen = SystemParameters.WorkArea;
        (Width, Height) = (Math.Min(Width, screen.Width), Math.Min(Height, screen.Height));
        SourceInitialized += (_, _) => TitleBar.Paint(this);
        AccentWave.Follow(this);
        DataContext = _model;
        _model.Startup.ConfirmDelete = ConfirmDeleteStartupItem;
        _model.Profiles.PickApp = PickApp;
        Loaded += async (_, _) =>
        {
            await _model.ConnectAsync();
            _tray = new TrayNotes(_model.Startup, ShowFromTray, () => _model.Client);
            // The first look after connecting takes note of what is already there.
            await _tray.CheckAsync();
            // Once per sign-in: the window starts with Windows, so this is the per-boot drift check.
            if (await _model.Drift!.CheckAsync() is { } drift)
                _tray.ShowDrift(drift, ShowDrift);
            await _model.CheckForUpdatesAsync();
        };
        Closed += (_, _) => _tray?.Dispose();
    }

    private void RestartIntoUpdate(object sender, RoutedEventArgs e) => _model.RestartIntoUpdate();

    private async void Retry(object sender, RoutedEventArgs e) => await _model.ConnectAsync();

    private bool ConfirmNotReviewed(IReadOnlyList<string> names) =>
        ConfirmDialog.Ask(
            this,
            $"These apps aren't in the reviewed catalogue:\n\n{string.Join("\n", names)}\n\n" +
            "Nobody has checked that they are free, safe or install silently. Install them only if you trust the publisher.\n\nInstall anyway?",
            "Not reviewed",
            ConfirmButtons.YesNo,
            ConfirmRisk.Ordinary) == ConfirmAnswer.Yes;

    private bool ConfirmUninstall(string name, string note) =>
        ConfirmDialog.Ask(this, $"Uninstall {name}?\n\n{note}".TrimEnd(), "Uninstall",
            ConfirmButtons.OkCancel, ConfirmRisk.Destructive) == ConfirmAnswer.Ok;

    /// <summary>The separate confirmation a destructive tweak needs. "No" is the default button.</summary>
    private bool ConfirmDestructive(TweakView tweak) => DestructiveTweakDialog(tweak).Ask() == ConfirmAnswer.Yes;

    /// <summary>The dialog <see cref="ConfirmDestructive"/> asks with, not yet shown. The screenshot tour shows it too.</summary>
    public ConfirmDialog DestructiveTweakDialog(TweakView tweak) =>
        new(
            this,
            $"{tweak.Name}\n\nThis can't be undone. {tweak.DestructiveWarning}\n\nDo it anyway?",
            "This can't be undone",
            ConfirmButtons.YesNo,
            ConfirmRisk.Destructive);

    /// <summary>The warning a tweak needs read before it is applied (OneDrive, the Store). "No" is the default button.</summary>
    private bool ConfirmWarning(TweakView tweak) =>
        ConfirmDialog.Ask(
            this,
            $"{tweak.Name}\n\n{tweak.ApplyWarning}\n\nGo ahead?",
            "Before you go on",
            ConfirmButtons.YesNo,
            ConfirmRisk.Ordinary) == ConfirmAnswer.Yes;

    /// <summary>Drivers can stop Windows from starting, so changing one is asked about first. "No" is the default.</summary>
    private bool ConfirmDriverChange(ServiceItemViewModel item, Elynt.Core.Tweaks.ServiceStart start) =>
        ConfirmDialog.Ask(
            this,
            $"{item.Title} is a driver. Setting it to {start} can stop Windows from starting if it is needed for your hardware.\n\n" +
            "The previous configuration is saved and can be put back from the Services page or the Undo history.\n\nChange it anyway?",
            "Change a driver",
            ConfirmButtons.YesNo,
            ConfirmRisk.Destructive) == ConfirmAnswer.Yes;

    /// <summary>The separate confirmation deleting a startup item needs. "No" is the default button.</summary>
    private bool ConfirmDeleteStartupItem(StartupItemView item) =>
        ConfirmDialog.Ask(
            this,
            $"Delete {item.Name} from startup?\n\n{item.Location}\n\nThis can't be undone. Turning it off instead stops it starting and can be undone.\n\nDelete it anyway?",
            "This can't be undone",
            ConfirmButtons.YesNo,
            ConfirmRisk.Destructive) == ConfirmAnswer.Yes;

    /// <summary>The separate confirmation destructive cleanup categories need. "No" is the default button.</summary>
    private bool ConfirmDestructiveCleanup(string warnings) =>
        ConfirmDialog.Ask(
            this,
            $"These can't be undone:\n\n{warnings}\n\nClean them anyway?",
            "This can't be undone",
            ConfirmButtons.YesNo,
            ConfirmRisk.Destructive) == ConfirmAnswer.Yes;

    private void ShowDrift()
    {
        ShowFromTray();
        _model.SelectedPage = MainViewModel.UndoHistory;
    }

    private bool ConfirmDestructiveReapply(string names) =>
        ConfirmDialog.Ask(
            this,
            $"Windows undid these, and putting them back can't be undone:\n\n{names}\n\nRe-apply them too? Choose No to re-apply only the rest.",
            "This can't be undone",
            ConfirmButtons.YesNo,
            ConfirmRisk.Destructive) == ConfirmAnswer.Yes;

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>The reboot prompt after a driver installer reported that Windows must restart. "Later" is the default.</summary>
    private void AskToReboot(DriverInstallResult result)
    {
        var answer = ConfirmDialog.Ask(
            this,
            $"{result.Message}\n\nRestart now? Save your work in other apps first.",
            "Restart to finish the driver install",
            ConfirmButtons.YesNo,
            ConfirmRisk.Ordinary);
        if (answer == ConfirmAnswer.Yes)
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
    }

    private string? PickGame() => FilePickers.Game(this);

    private string? PickApp() => FilePickers.AppToClose(this);
}
