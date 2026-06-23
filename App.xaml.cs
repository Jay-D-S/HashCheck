using System.Reflection;
using System.Threading;
using HashCheck.Core.Scheduling;
using HashCheck.Core.Volumes;
using HashCheck.Services;
using HashCheck.Tray;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HashCheck;

/// <summary>Application entry point. Enforces single-instance via a named mutex, initialises services, hosts the tray icon, and drives the reminder/autoscan lifecycle.</summary>
public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;
    private static Mutex? _mutex;
    private TrayIconHost? _tray;

    public App()
    {
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        InitializeComponent();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        LogCrash(e.Exception);
    }

    private static void OnDomainException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogCrash(e.ExceptionObject as Exception);
    }

    private static void LogCrash(Exception? ex)
    {
        var text = ex?.ToString() ?? "Unknown exception";
        System.Diagnostics.Debug.WriteLine("=== HASHCHECK CRASH ===\n" + text);
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "HashCheck_crash.txt");
            File.WriteAllText(path, text);
            System.Diagnostics.Debug.WriteLine("Crash log written to: " + path);
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Named mutex ensures only one instance runs; the second instance exits immediately.
        _mutex = new Mutex(true, "HashCheck-SingleInstance-{A4E2F3B0-1234-5678-ABCD-EF1234567890}", out bool isNew);
        if (!isNew)
        {
            _mutex = null;
            Current.Exit();
            return;
        }

        AppServices.Initialize();

        MainWindow = new MainWindow();
        MainWindow.Activate();

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
            _tray = new TrayIconHost(hwnd);
            _tray.ShowDashboardRequested += () => ShowAndNavigate("dashboard");
            _tray.CreateHashRequested += () => ShowAndNavigate("create");
            _tray.SettingsRequested += () => ShowAndNavigate("settings");
            _tray.AboutRequested += () => MainWindow.DispatcherQueue.TryEnqueue(async () => await ShowAboutDialogAsync());
            _tray.ExitRequested += () => { _tray?.Dispose(); Current.Exit(); };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Tray init failed: " + ex.Message);
        }

        AppServices.Scheduler.RemindersAvailable += OnRemindersAvailable;
        AppServices.Scheduler.VolumeAttached += OnVolumeAttached;
        AppServices.Scheduler.Start();

        if (MainWindow is MainWindow mw)
            mw.SetTitle(BuildWindowTitle());
    }

    private void ShowAndNavigate(string page)
    {
        MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            GetAppWindow(MainWindow)?.Show();
            MainWindow.Activate();
            if (MainWindow is MainWindow mw)
                mw.NavigateTo(page);
        });
    }

    private void OnRemindersAvailable(IReadOnlyList<ReminderItem> items)
    {
        MainWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var names = string.Join(", ", items.Take(3).Select(i => i.HashFile.MediaName));
                GetAppWindow(MainWindow)?.Show();
                MainWindow.Activate();

                var xamlRoot = MainWindow.Content?.XamlRoot;
                if (xamlRoot == null) return;

                var dlg = new ContentDialog
                {
                    Title = "Validation Due",
                    Content = $"The following media are due for verification:\n{names}",
                    PrimaryButtonText = "Validate Now",
                    SecondaryButtonText = "Snooze 7 days",
                    CloseButtonText = "Dismiss",
                    XamlRoot = xamlRoot
                };

                var result = await dlg.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    ShowAndNavigate("validate");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Reminder dialog error: " + ex.Message);
            }
        });
    }

    private void OnVolumeAttached(VolumeAttachedEventArgs args)
    {
        // Check the setting before showing the dialog
        if (!AppServices.Settings.Current.AutoscanPromptOnAttach) return;

        MainWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var xamlRoot = MainWindow.Content?.XamlRoot;
                if (xamlRoot == null) return;

                var groupNames = string.Join(", ", args.HashSets.Select(h => h.MediaName).Distinct());

                // Delay options: (display label, delay — null means "scan now", Missing means skip)
                var options = new (string Label, TimeSpan? Delay)[]
                {
                    ("Scan now",      TimeSpan.Zero),
                    ("In 5 minutes",  TimeSpan.FromMinutes(5)),
                    ("In 30 minutes", TimeSpan.FromMinutes(30)),
                    ("In 1 hour",     TimeSpan.FromHours(1)),
                    ("In 4 hours",    TimeSpan.FromHours(4)),
                    ("In 8 hours",    TimeSpan.FromHours(8)),
                    ("In 24 hours",   TimeSpan.FromHours(24)),
                    ("I'll do it manually", null),
                };

                var radioButtons = new RadioButtons { SelectedIndex = 3 }; // Default: 1 hour
                foreach (var (label, _) in options)
                    radioButtons.Items.Add(label);

                var dlg = new ContentDialog
                {
                    Title = $"Media Attached: {args.Label}",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Used by: {groupNames}\n\nWhen would you like to run autoscan?",
                                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                            },
                            radioButtons
                        }
                    },
                    PrimaryButtonText = "Schedule",
                    CloseButtonText = "Skip",
                    XamlRoot = xamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dlg.ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                var idx = radioButtons.SelectedIndex;
                if (idx < 0 || idx >= options.Length) return;
                var delay = options[idx].Delay;
                if (delay == null) return; // "I'll do it manually"

                _ = RunDelayedAutoscanAsync(args, delay.Value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("VolumeAttached dialog error: " + ex.Message);
            }
        });
    }

    private static async Task RunDelayedAutoscanAsync(VolumeAttachedEventArgs args, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);

            // Confirm volume is still connected
            var vol = VolumeLocator.FindBySerial(args.Serial);
            if (vol == null) return;

            foreach (var hashSet in args.HashSets)
            {
                await AppServices.HashSets.AutoscanAsync(
                    hashSet.FilePath, vol.RootPath, null, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Delayed autoscan error: " + ex.Message);
        }
    }

    private static readonly string[] NagMessages =
    {
        "Buy me a beer",
        "Buy me a coffee",
        "help me pay for electricity",
        "help me pay my bills",
        "help me pay for hosting",
        "help me go on holiday",
        "help me pay for Claude",
    };

    private static string GetAppVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v?.ToString(3) ?? "1.0.0";
        }
    }

    private static string BuildWindowTitle()
    {
        var version = GetAppVersion();
        var s = AppServices.Settings.Current;

        if (s.HideDonationNag)
            return $"HashCheck v{version}";

        // Advance the nag index each launch so a different message appears every time
        var nag = NagMessages[s.NagMessageIndex % NagMessages.Length];
        s.NagMessageIndex = (s.NagMessageIndex + 1) % NagMessages.Length;
        AppServices.Settings.Save();

        return $"HashCheck v{version} — {nag}";
    }

    private async Task ShowAboutDialogAsync()
    {
        var xamlRoot = MainWindow.Content?.XamlRoot;
        if (xamlRoot == null) return;

        GetAppWindow(MainWindow)?.Show();
        MainWindow.Activate();

        var version = GetAppVersion();

        var dlg = new ContentDialog
        {
            Title = $"HashCheck v{version}",
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 460,
                Children =
                {
                    new TextBlock
                    {
                        Text = "© 2025 Jason Sutton. All rights reserved.",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        Text =
                            "HashCheck is provided \"as is\", without warranty of any kind, express or " +
                            "implied, including but not limited to the warranties of merchantability, " +
                            "fitness for a particular purpose, or non-infringement.\n\n" +
                            "In no event shall the author be liable for any claim, damages, or other " +
                            "liability — including data loss or corruption — arising from the use of or " +
                            "inability to use this software. Use HashCheck at your own risk and always " +
                            "maintain independent backups of your data.\n\n" +
                            "If HashCheck has saved you time or protected your files, a small donation " +
                            "is always appreciated — it helps cover hosting, tools, and the occasional " +
                            "cup of coffee!"
                    }
                }
            },
            PrimaryButtonText = "Donate ❤️",
            CloseButtonText = "Close",
            XamlRoot = xamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dlg.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://www.paypal.me/jasondsutton")
            { UseShellExecute = true });

            // Suppress the nag — we trust the user clicked because they donated
            AppServices.Settings.Current.HideDonationNag = true;
            AppServices.Settings.Save();

            if (MainWindow is MainWindow mw)
                mw.SetTitle($"HashCheck v{GetAppVersion()}");
        }
    }

    /// <summary>Retrieves the WinUI3 <see cref="Microsoft.UI.Windowing.AppWindow"/> for a given <see cref="Window"/> via its HWND. Returns <c>null</c> if the window is not yet created.</summary>
    private static Microsoft.UI.Windowing.AppWindow? GetAppWindow(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
        }
        catch { return null; }
    }

    ~App()
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
