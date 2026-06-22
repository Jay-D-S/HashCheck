namespace HashCheck.Core.Validation;

public sealed class PauseToken
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsPaused => _gate.CurrentCount == 0;

    // Called between files in the validation loop — suspends until Resume().
    public async Task WaitIfPausedAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        _gate.Release();
    }

    public void Pause()
    {
        if (!IsPaused) _gate.Wait();
    }

    public void Resume()
    {
        if (IsPaused) _gate.Release();
    }
}
