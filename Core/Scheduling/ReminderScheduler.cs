using HashCheck.Core.HashFile;

namespace HashCheck.Core.Scheduling;

public record ReminderItem(HashFileData HashFile, DateTime DueDate);

public static class ReminderScheduler
{
    public static IEnumerable<ReminderItem> GetOverdueItems(IEnumerable<HashFileData> hashFiles)
    {
        var now = DateTime.UtcNow;
        foreach (var hf in hashFiles)
        {
            if (now >= hf.DueDate)
                yield return new ReminderItem(hf, hf.DueDate);
        }
    }

    public static DateTime ComputeDueDate(HashFileData hashFile) => hashFile.DueDate;

    public static TimeSpan TimeUntilDue(HashFileData hashFile)
    {
        var remaining = hashFile.DueDate - DateTime.UtcNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
