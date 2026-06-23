using System.Security.Cryptography;
using System.Text;

namespace HashCheck.Core.HashFile;

/// <summary>Thrown when the SHA-256 checksum in <c>[INTEGRITY]</c> does not match the file content.</summary>
public class HashFileIntegrityException(string message) : Exception(message);
/// <summary>Thrown when a <c>.hash</c> file cannot be parsed (missing magic header, malformed fields, etc.).</summary>
public class HashFileParseException(string message) : Exception(message);

/// <summary>Reads and parses a <c>.hash</c> file from disk, with optional integrity verification.</summary>
public static class HashFileReader
{
    private const string Magic = "HASHCHECK/1.0";

    /// <summary>Reads the file at <paramref name="filePath"/>, optionally verifying the <c>[INTEGRITY]</c> SHA-256 checksum first.</summary>
    public static async Task<HashFileData> ReadAsync(string filePath, bool verifyIntegrity = true)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        // Skip BOM if present — the writer never emits one, but guard defensively
        int textStart = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        var text = Encoding.UTF8.GetString(bytes, textStart, bytes.Length - textStart);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        if (verifyIntegrity)
            VerifyIntegrity(bytes, lines, textStart);

        return Parse(filePath, lines);
    }

    /// <summary>Verifies the SHA-256 checksum stored in <c>[INTEGRITY]</c> against the raw bytes preceding that section. Always SHA-256 regardless of the content hash algorithm.</summary>
    private static void VerifyIntegrity(byte[] fileBytes, string[] lines, int textStart)
    {
        int integrityLineIndex = -1;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i] == "[INTEGRITY]") { integrityLineIndex = i; break; }
        }

        if (integrityLineIndex < 0)
            throw new HashFileIntegrityException("Missing [INTEGRITY] section.");

        string? storedHash = null;
        for (int i = integrityLineIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("SHA-256:", StringComparison.Ordinal))
            {
                storedHash = lines[i]["SHA-256:".Length..];
                break;
            }
        }

        if (storedHash == null)
            throw new HashFileIntegrityException("Missing SHA-256 value in [INTEGRITY] section.");

        var integrityMarker = Encoding.UTF8.GetBytes("\n[INTEGRITY]");
        int cutoff = -1;
        for (int i = fileBytes.Length - integrityMarker.Length; i >= 0; i--)
        {
            bool match = true;
            for (int j = 0; j < integrityMarker.Length; j++)
            {
                if (fileBytes[i + j] != integrityMarker[j]) { match = false; break; }
            }
            if (match) { cutoff = i + 1; break; }
        }

        if (cutoff < 0)
            throw new HashFileIntegrityException("Cannot locate [INTEGRITY] section in file bytes.");

        // Hash only the bytes up to (but not including) the "\n[INTEGRITY]" marker
        var hash = Convert.ToHexString(SHA256.HashData(fileBytes.AsSpan(textStart, cutoff - textStart))).ToLowerInvariant();
        if (!hash.Equals(storedHash, StringComparison.OrdinalIgnoreCase))
            throw new HashFileIntegrityException("Integrity check failed. File may be corrupted.");
    }

    private static HashFileData Parse(string filePath, string[] lines)
    {
        if (lines.Length == 0 || lines[0] != Magic)
            throw new HashFileParseException($"Not a valid HashCheck file (expected '{Magic}').");

        var data = new HashFileData { FilePath = filePath };
        string currentSection = "";
        string currentDirectory = "";

        foreach (var rawLine in lines.Skip(1))
        {
            var line = rawLine;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1];
                currentDirectory = "";
                continue;
            }

            if (string.IsNullOrEmpty(line)) continue;

            switch (currentSection)
            {
                case "META":
                    var eq = line.IndexOf('=');
                    if (eq < 0) break;
                    ParseMeta(data, line[..eq], line[(eq + 1)..]);
                    break;

                case "VOLUMES":
                    var ve = ParseVolumeEntry(line);
                    if (ve != null) data.Volumes.Add(ve);
                    break;

                case "FILTERS":
                    data.Filters.Add(line);
                    break;

                case "VALIDATIONS":
                    var entry = ParseValidation(line);
                    if (entry != null) data.Validations.Add(entry);
                    break;

                case "PATHS":
                    data.Paths.Add(line);
                    break;

                case "FILES":
                    if (line.StartsWith('\\') && !line.Contains('|'))
                        currentDirectory = line;
                    else
                    {
                        var fe = ParseFileEntry(currentDirectory, line);
                        if (fe != null) data.Files.Add(fe);
                    }
                    break;

                case "INTEGRITY":
                    break;
            }
        }

        return data;
    }

    private static void ParseMeta(HashFileData data, string key, string val)
    {
        switch (key)
        {
            case "Description": data.Description = val; break;
            case "MediaName": data.MediaName = val; break;
            case "Algorithm":
                if (Enum.TryParse<HashAlgorithmType>(val, out var algo)) data.Algorithm = algo;
                break;
            case "ReminderDays": data.ReminderDays = int.TryParse(val, out var d) ? d : 180; break;
            case "FilterMode":
                if (Enum.TryParse<FilterMode>(val, out var fm)) data.FilterMode = fm;
                break;
            case "Autoscan":
                if (bool.TryParse(val, out var autoscan)) data.Autoscan = autoscan;
                break;
            case "DateCreated":
                if (DateTime.TryParse(val, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dc))
                    data.DateCreated = dc;
                break;
            case "DateModified":
                if (DateTime.TryParse(val, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dm))
                    data.DateModified = dm;
                break;
        }
    }

    private static VolumeEntry? ParseVolumeEntry(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 4) return null;
        if (!long.TryParse(parts[2], out var totalBytes)) return null;
        if (!DateTime.TryParse(parts[3], null, System.Globalization.DateTimeStyles.RoundtripKind, out var dateAdded)) return null;
        var scanSubPath = parts.Length >= 5 && !string.IsNullOrEmpty(parts[4]) ? parts[4] : @"\";
        return new VolumeEntry(parts[0], parts[1], totalBytes, dateAdded, scanSubPath);
    }

    private static ValidationEntry? ParseValidation(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 2) return null;
        if (!DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts)) return null;
        var status = parts[1];

        string volumeSerial = "";
        var stats = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts.Skip(2))
        {
            if (p.StartsWith("volume=", StringComparison.OrdinalIgnoreCase))
            {
                volumeSerial = p["volume=".Length..];
                continue;
            }
            var eq = p.IndexOf('=');
            if (eq > 0 && long.TryParse(p[(eq + 1)..], out var n))
                stats[p[..eq]] = n;
        }

        return new ValidationEntry(
            ts, status, volumeSerial,
            (int)stats.GetValueOrDefault("files"),
            stats.GetValueOrDefault("bytes"),
            (int)stats.GetValueOrDefault("matching"),
            (int)stats.GetValueOrDefault("notmatching"),
            (int)stats.GetValueOrDefault("missing"),
            (int)stats.GetValueOrDefault("errors"));
    }

    private static FileEntry? ParseFileEntry(string dir, string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 4) return null;
        if (!long.TryParse(parts[2], out var size)) return null;
        if (!DateTime.TryParse(parts[3], null, System.Globalization.DateTimeStyles.RoundtripKind, out var mtime)) return null;

        // Files under the scan root itself are stored under "\" directory header
        var relativePath = dir == "\\" || dir == "/"
            ? "\\" + parts[0]
            : dir + "\\" + parts[0];

        return new FileEntry(relativePath, parts[1], size, mtime);
    }
}
