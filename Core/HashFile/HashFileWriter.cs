using System.Security.Cryptography;
using System.Text;

namespace HashCheck.Core.HashFile;

public static class HashFileWriter
{
    public static async Task WriteAsync(HashFileData data, string filePath)
    {
        var content = BuildContent(data);
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var integrityHash = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();

        var full = content + "[INTEGRITY]\nSHA-256:" + integrityHash + "\n";

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, full, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, filePath, overwrite: true);
    }

    private static string BuildContent(HashFileData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("HASHCHECK/1.0");

        sb.AppendLine("[META]");
        sb.AppendLine($"Description={data.Description}");
        sb.AppendLine($"MediaName={data.MediaName}");
        sb.AppendLine($"Algorithm={data.Algorithm}");
        sb.AppendLine($"ReminderDays={data.ReminderDays}");
        sb.AppendLine($"FilterMode={data.FilterMode}");
        sb.AppendLine($"Autoscan={data.Autoscan}");
        sb.AppendLine($"DateCreated={data.DateCreated:O}");
        sb.AppendLine($"DateModified={data.DateModified:O}");

        sb.AppendLine("[VOLUMES]");
        foreach (var v in data.Volumes)
            sb.AppendLine(FormatVolume(v));

        sb.AppendLine("[FILTERS]");
        foreach (var f in data.Filters)
            sb.AppendLine(f);

        sb.AppendLine("[VALIDATIONS]");
        foreach (var v in data.Validations)
            sb.AppendLine(FormatValidation(v));

        sb.AppendLine("[PATHS]");
        foreach (var p in data.Paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine(p);

        sb.AppendLine("[FILES]");
        var byDir = data.Files
            .GroupBy(f => GetDirectory(f.RelativePath))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byDir)
        {
            sb.AppendLine(group.Key);
            foreach (var file in group.OrderBy(f => GetFileName(f.RelativePath), StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"{GetFileName(file.RelativePath)}|{file.Hash}|{file.SizeBytes}|{file.ModifiedUtc:O}");
        }

        return sb.ToString();
    }

    private static string FormatVolume(VolumeEntry v) =>
        $"{v.SerialNumber}|{v.Label}|{v.TotalBytes}|{v.DateAdded:O}|{v.ScanSubPath}";

    private static string FormatValidation(ValidationEntry v) =>
        $"{v.Timestamp:O}|{v.Status}|volume={v.VolumeSerial}|files={v.Files}|bytes={v.Bytes}|matching={v.Matching}|notmatching={v.NotMatching}|missing={v.Missing}|errors={v.Errors}";

    private static string GetDirectory(string relativePath)
    {
        var slash = relativePath.LastIndexOf('\\');
        return slash <= 0 ? "\\" : relativePath[..slash];
    }

    private static string GetFileName(string relativePath)
    {
        var slash = relativePath.LastIndexOf('\\');
        return slash < 0 ? relativePath : relativePath[(slash + 1)..];
    }
}
