using HashCheck.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace HashCheck;

public sealed partial class MainWindow : Window
{
    private bool _realClose = false;
    private AppWindow? _appWindow;
    private bool _suppressNavigation;

    public MainWindow()
    {
        InitializeComponent();
        SetupWindow();
        ContentFrame.Navigate(typeof(DashboardPage));

        _suppressNavigation = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressNavigation = false;
    }

    private void SetupWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(id);

            if (_appWindow != null)
            {
                _appWindow.Resize(new Windows.Graphics.SizeInt32(1100, 700));
                _appWindow.Title = "HashCheck";

                var iconPath = Path.Combine(AppContext.BaseDirectory, "HashIcon.ico");
                if (File.Exists(iconPath))
                    _appWindow.SetIcon(iconPath);

                _appWindow.Closing += (_, args) =>
                {
                    if (!_realClose)
                    {
                        args.Cancel = true;
                        _appWindow.Hide();
                    }
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("SetupWindow failed: " + ex.Message);
        }
    }

    public void NavigateTo(string tag)
    {
        var type = tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "create" => typeof(CreateHashPage),
            "validate" => typeof(ValidatePage),
            "settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        var item = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>())
            .FirstOrDefault(i => (string?)i.Tag == tag);

        _suppressNavigation = true;
        if (item != null) NavView.SelectedItem = item;
        _suppressNavigation = false;

        if (ContentFrame.CurrentSourcePageType != type)
            ContentFrame.Navigate(type, null, new EntranceNavigationTransitionInfo());
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigation) return;

        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag as string ?? "dashboard";
            var type = tag switch
            {
                "dashboard" => typeof(DashboardPage),
                "create" => typeof(CreateHashPage),
                "validate" => typeof(ValidatePage),
                "settings" => typeof(SettingsPage),
                _ => typeof(DashboardPage)
            };

            if (ContentFrame.CurrentSourcePageType != type)
                ContentFrame.Navigate(type, null, new EntranceNavigationTransitionInfo());
        }
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    public void SetRealClose() => _realClose = true;

    public void SetTitle(string title)
    {
        if (_appWindow != null) _appWindow.Title = title;
    }
}
