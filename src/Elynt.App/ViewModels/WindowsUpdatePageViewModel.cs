using Elynt.Core.Ipc;
using Elynt.Core.UpdateControl;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Windows Update page's "Check and install now": starts the run in the service, then follows its progress
/// until it finishes. Works while updates are paused.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
public sealed class WindowsUpdatePageViewModel(Func<EnginePipeClient?> client) : ObservableObject
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);
    private UpdateRunStatus _status = UpdateRunStatus.Idle;
    private bool _following;

    public UpdateRunStatus Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(Message));
                OnPropertyChanged(nameof(Percent));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public string Message => Status.Phase == UpdatePhase.Idle
        ? "Installs the monthly quality and security updates now, even while updates are paused. Drivers and feature updates are left alone."
        : Status.Message;

    public int Percent => Status.Percent;

    public bool IsRunning => Status.Running;

    public bool CanStart => !IsRunning && !_following;

    public async Task StartAsync()
    {
        if (client() is not { } pipe || !CanStart)
            return;
        try
        {
            Status = await pipe.StartWindowsUpdateAsync();
            await FollowAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Check and install now failed");
            Status = new UpdateRunStatus(UpdatePhase.Failed, $"Couldn't reach the service: {ex.Message}");
        }
    }

    /// <summary>Picks up a run already going, e.g. started from another window, when the page opens.</summary>
    public async Task RefreshAsync()
    {
        if (client() is not { } pipe || _following)
            return;
        try
        {
            Status = await pipe.GetWindowsUpdateStatusAsync();
            await FollowAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't read the Windows Update progress");
        }
    }

    private async Task FollowAsync()
    {
        _following = true;
        OnPropertyChanged(nameof(CanStart));
        try
        {
            while (Status.Running && client() is { } pipe)
            {
                await Task.Delay(PollEvery);
                Status = await pipe.GetWindowsUpdateStatusAsync();
            }
        }
        finally
        {
            _following = false;
            OnPropertyChanged(nameof(CanStart));
        }
    }
}
