using HashCheck.Core.HashFile;

namespace HashCheck.Core.Scheduling;

/// <summary>A hash set that has passed its validation due date.</summary>
public record ReminderItem(HashFileData HashFile, DateTime DueDate);

/// <summary>Pure-logic helper for computing validation due dates and finding overdue hash sets.</summary>
public static class ReminderScheduler
{
    /// <summary>Returns all hash files whose <see cref="HashFileData.DueDate"/> is in the past.</summary>
    public static IEnumerable<ReminderItem> GetOverdueItems(IEnumerable<HashFileData> hashFiles)
    {
        var now = DateTime.UtcNow;
        foreach (var hf in hashFiles)
        {
            if (now >= hf.DueDate)
                yield return new ReminderItem(hf, hf.DueDate);
        }
    }

    /// <summary>Returns the absolute UTC date/time at which a new validation is due.</summary>
    public static DateTime ComputeDueDate(HashFileData hashFile) => hashFile.DueDate;

    /// <summary>Returns how long until the next validation is due, clamped to zero if already overdue.</summary>
    public static TimeSpan TimeUntilDue(HashFileData hashFile)
    {
        var remaining = hashFile.DueDate - DateTime.UtcNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
