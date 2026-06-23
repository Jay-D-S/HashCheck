using HashCheck.Core.HashFile;
using HashCheck.Core.Scheduling;
using HashCheck.Core.Volumes;

namespace HashCheck.Services;

/// <summary>Event data for a newly-detected volume mount. Carries identity info and the hash sets that track that volume and have <c>Autoscan=True</c>.</summary>
public sealed class VolumeAttachedEventArgs(
    string serial, string label, string rootPath,
    IReadOnlyList<HashFileData> hashSets)
{
    public string Serial { get; } = serial;
    public string Label { get; } = label;
    public string RootPath { get; } = rootPath;
    public IReadOnlyList<HashFileData> HashSets { get; } = hashSets;
}

/// <summary>Background service that polls for overdue validations (every 24 h) and newly-mounted volumes (every 30 s). Raises events on the calling thread for the UI to handle.</summary>
public sealed class SchedulerService : IDisposable
{
    private readonly HashSetService _hashSets;
    private Timer? _reminderTimer;
    private Timer? _volumeTimer;
    private readonly HashSet<string> _knownOnlineSerials = new(StringComparer.OrdinalIgnoreCase);
    // True only for the very first volume tick — used to snapshot the baseline without firing attach events
    private bool _initialVolumeScan = true;

    /// <summary>Raised when one or more hash sets are overdue for validation.</summary>
    public event Action<IReadOnlyList<ReminderItem>>? RemindersAvailable;
    /// <summary>Raised when a volume that has autoscan-enabled hash sets is newly mounted (not present at startup).</summary>
    public event Action<VolumeAttachedEventArgs>? VolumeAttached;

    public SchedulerService(HashSetService hashSets) => _hashSets = hashSets;

    /// <summary>Starts the reminder and volume-poll timers. Must be called after the UI is initialised so event handlers can marshal back to the dispatcher queue.</summary>
    public void Start()
    {
        // Delay the first reminder check by 5 s — XamlRoot on the main window is not set until
        // after the first frame renders, so firing immediately (TimeSpan.Zero) would silently drop
        // the dialog via the "if (xamlRoot == null) return" guard in OnRemindersAvailable.
        _reminderTimer = new Timer(OnReminderTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromHours(24));
        // Delay the first volume check by 10 s to let the window and tray settle before firing events
        _volumeTimer = new Timer(OnVolumeTick, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    private async void OnVolumeTick(object? state)
    {
        try
        {
            var currentOnline = VolumeLocator.GetAllVolumes()
                .Select(v => v.SerialNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // First tick: just record the baseline — don't fire events for already-mounted drives
            if (_initialVolumeScan)
            {
                _initialVolumeScan = false;
                lock (_knownOnlineSerials) _knownOnlineSerials.UnionWith(currentOnline);
                return;
            }

            List<string> newlyAttached;
            lock (_knownOnlineSerials)
            {
                newlyAttached = currentOnline.Except(_knownOnlineSerials).ToList();
                _knownOnlineSerials.Clear();
                _knownOnlineSerials.UnionWith(currentOnline);
            }

            if (newlyAttached.Count == 0) return;

            var all = await _hashSets.LoadAllKnownAsync();
            foreach (var serial in newlyAttached)
            {
                var matchingHashSets = all
                    .Where(hf => hf.Autoscan && hf.Volumes.Any(v =>
                        v.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (matchingHashSets.Count == 0) continue;

                var vol = VolumeLocator.FindBySerial(serial);
                if (vol == null) continue;

                VolumeAttached?.Invoke(
                    new VolumeAttachedEventArgs(serial, vol.Label, vol.RootPath, matchingHashSets));
            }
        }
        catch { }
    }

    private async void OnReminderTick(object? state)
    {
        try
        {
            var all = await _hashSets.LoadAllKnownAsync();
            var overdue = ReminderScheduler.GetOverdueItems(all).ToList();
            if (overdue.Count > 0)
                RemindersAvailable?.Invoke(overdue);
        }
        catch { }
    }

    public void Dispose()
    {
        _reminderTimer?.Dispose();
        _volumeTimer?.Dispose();
    }
}
