namespace HashCheck.Core.Validation;

/// <summary>
/// Cooperative pause mechanism for the validation loop.
/// Uses a <see cref="SemaphoreSlim"/>(1,1) as an async gate:
/// <see cref="Pause"/> takes the only permit (blocking new waiters),
/// and <see cref="Resume"/> releases it (unblocking them).
/// </summary>
public sealed class PauseToken
{
    // A semaphore with a single permit acts as a binary gate:
    // when count == 0 the gate is closed (paused); count == 1 means open (running).
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary><c>true</c> while the gate is closed (Pause has been called without a matching Resume).</summary>
    public bool IsPaused => _gate.CurrentCount == 0;

    /// <summary>Called between files in the validation loop — suspends asynchronously until <see cref="Resume"/> is called.</summary>
    public async Task WaitIfPausedAsync(CancellationToken ct)
    {
        // Acquire then immediately release: if paused the acquire blocks until Resume releases the permit.
        await _gate.WaitAsync(ct);
        _gate.Release();
    }

    /// <summary>Closes the gate, blocking subsequent <see cref="WaitIfPausedAsync"/> calls.</summary>
    public void Pause()
    {
        if (!IsPaused) _gate.Wait();
    }

    /// <summary>Opens the gate, allowing the validation loop to continue from its next <see cref="WaitIfPausedAsync"/> call.</summary>
    public void Resume()
    {
        if (IsPaused) _gate.Release();
    }
}
