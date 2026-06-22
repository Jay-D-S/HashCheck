using HashCheck.Core.Settings;
using HashCheck.Services;

namespace HashCheck;

public static class AppServices
{
    public static SettingsStore Settings { get; private set; } = null!;
    public static HashSetService HashSets { get; private set; } = null!;
    public static SchedulerService Scheduler { get; private set; } = null!;

    public static void Initialize()
    {
        Settings = new SettingsStore();
        Settings.Load();
        HashSets = new HashSetService(Settings);
        Scheduler = new SchedulerService(HashSets);
    }
}
