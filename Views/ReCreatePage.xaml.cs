using HashCheck.Core.Scanning;
using HashCheck.Core.Volumes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HashCheck.Views;

/// <summary>Code-behind for the Re-create (full re-baseline) page.</summary>
public sealed partial class ReCreatePage : Page
{
    private string? _hashFilePath;
    private string? _mediaSerial;
    private string? _mediaRoot;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _pollCts;

    public ReCreatePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _hashFilePath = e.Parameter as string;
        if (_hashFilePath == null) return;

        var hashFile = await Core.HashFile.HashFileReader.ReadAsync(_hashFilePath, verifyIntegrity: false);
        _mediaSerial = hashFile.SerialNumber;
        InsertMediaText.Text = $"Please insert media: {hashFile.MediaName} ({hashFile.SerialNumber})";

        // Populate drive picker
        foreach (var vol in VolumeLocator.GetAllVolumes())
            DrivePicker.Items.Add($"{vol.RootPath.TrimEnd('\\')} ({vol.Label})");

        var vol2 = VolumeLocator.FindBySerial(hashFile.SerialNumber);
        if (vol2 != null)
        {
            _mediaRoot = vol2.RootPath;
            await StartReCreateAsync();
        }
        else
        {
            PollingRing.IsActive = true;
            _pollCts = new CancellationTokenSource();
            _ = PollAsync(_pollCts.Token);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct).ConfigureAwait(false);
            var vol = VolumeLocator.FindBySerial(_mediaSerial!);
            if (vol != null)
            {
                _mediaRoot = vol.RootPath;
                DispatcherQueue.TryEnqueue(async () => await StartReCreateAsync());
                return;
            }
        }
    }

    private async void UseManualDrive_Click(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        var selected = DrivePicker.SelectedItem?.ToString();
        if (selected == null) return;
        _mediaRoot = selected.Split(' ')[0] + "\\";
        await StartReCreateAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        Frame.Navigate(typeof(DashboardPage));
    }

    private async Task StartReCreateAsync()
    {
        _pollCts?.Cancel();
        InsertMediaPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;

        _cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            FilesBar.Value = p.FilesProcessed;
            FilesBar.Maximum = p.FilesTotal > 0 ? p.FilesTotal : 1;
            FilesText.Text = $"Files: {p.FilesProcessed} / {p.FilesTotal}";
            BytesBar.Value = p.BytesProcessed;
            BytesBar.Maximum = p.BytesTotal > 0 ? p.BytesTotal : 1;
            BytesText.Text = $"Bytes: {p.BytesProcessed:N0} / {p.BytesTotal:N0}";
            CurrentFileText.Text = p.CurrentFile;
            EtaText.Text = p.Eta.HasValue ? $"ETA: {p.Eta.Value:mm\\:ss}" : "";
        });

        try
        {
            await AppServices.HashSets.ReCreateAsync(_hashFilePath!, _mediaRoot!, progress, _cts.Token);
            ShowDone(true, "Hash set re-created successfully.");
        }
        catch (OperationCanceledException)
        {
            ShowDone(false, "Operation cancelled.");
        }
        catch (Exception ex)
        {
            ShowDone(false, $"Error: {ex.Message}");
        }
    }

    private void ShowDone(bool success, string message)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        ResultIcon.Glyph = success ? "" : ""; // Checkmark or Error
        ResultText.Text = message;
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void GoToDashboard_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(DashboardPage));
    }
}
