using HashCheck.Core.Settings;
using HashCheck.Services;
using HashCheck.ViewModels;

namespace HashCheck;

/// <summary>Static service locator providing access to the three application-wide singletons. Initialised once in <see cref="App.OnLaunched"/>.</summary>
public static class AppServices
{
    public static SettingsStore Settings { get; private set; } = null!;
    public static HashSetService HashSets { get; private set; } = null!;
    public static SchedulerService Scheduler { get; private set; } = null!;

    /// <summary>The currently-running validation, if any. Set by <see cref="ValidateViewModel"/> when validation starts and cleared when all rows finish. Lets the user navigate away and return to the same in-progress view.</summary>
    public static ValidateViewModel? ActiveValidation { get; set; }

    /// <summary>Creates and wires up all services. Must be called before any other <see cref="AppServices"/> access.</summary>
    public static void Initialize()
    {
        Settings = new SettingsStore();
        Settings.Load();
        HashSets = new HashSetService(Settings);
        Scheduler = new SchedulerService(HashSets);
    }
}
