using HashCheck.Core.HashFile;
using HashCheck.Core.Scheduling;
using HashCheck.Core.Volumes;

namespace HashCheck.Services;

public sealed class VolumeAttachedEventArgs(
    string serial, string label, string rootPath,
    IReadOnlyList<HashFileData> hashSets)
{
    public string Serial   { get; } = serial;
    public string Label    { get; } = label;
    public string RootPath { get; } = rootPath;
    public IReadOnlyList<HashFileData> HashSets { get; } = hashSets;
}

public sealed class SchedulerService : IDisposable
{
    private readonly HashSetService _hashSets;
    private Timer? _reminderTimer;
    private Timer? _volumeTimer;
    private readonly HashSet<string> _knownOnlineSerials = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialVolumeScan = true;

    public event Action<IReadOnlyList<ReminderItem>>? RemindersAvailable;
    public event Action<VolumeAttachedEventArgs>? VolumeAttached;

    public SchedulerService(HashSetService hashSets) => _hashSets = hashSets;

    public void Start()
    {
        _reminderTimer = new Timer(OnReminderTick, null, TimeSpan.Zero, TimeSpan.FromHours(24));
        // First volume check after 10s (let app settle), then every 30s
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
