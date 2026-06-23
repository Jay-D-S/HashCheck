using HashCheck.Core.Settings;
using HashCheck.Services;

namespace HashCheck;

/// <summary>Static service locator providing access to the three application-wide singletons. Initialised once in <see cref="App.OnLaunched"/>.</summary>
public static class AppServices
{
    public static SettingsStore Settings { get; private set; } = null!;
    public static HashSetService HashSets { get; private set; } = null!;
    public static SchedulerService Scheduler { get; private set; } = null!;

    /// <summary>Creates and wires up all services. Must be called before any other <see cref="AppServices"/> access.</summary>
    public static void Initialize()
    {
        Settings = new SettingsStore();
        Settings.Load();
        HashSets = new HashSetService(Settings);
        Scheduler = new SchedulerService(HashSets);
    }
}
