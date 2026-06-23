using System.Runtime.InteropServices;
using System.Text;

namespace HashCheck.Core.Volumes;

/// <summary>Discovers mounted volumes via P/Invoke <c>GetVolumeInformation</c> and <see cref="DriveInfo"/>. Always identifies volumes by serial number, not drive letter.</summary>
public static class VolumeLocator
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    /// <summary>Returns identity information for the volume mounted at <paramref name="rootPath"/> (e.g. <c>D:\</c>), or <c>null</c> if the volume cannot be read. Assigns a sensible fallback label when the volume has none.</summary>
    public static VolumeIdentity? GetVolumeIdentity(string rootPath)
    {
        try
        {
            var labelBuf = new StringBuilder(261);
            if (!GetVolumeInformation(rootPath, labelBuf, (uint)labelBuf.Capacity,
                    out uint serial, out _, out _, null, 0))
                return null;

            var drive = new DriveInfo(rootPath);
            var label = labelBuf.ToString();
            if (string.IsNullOrEmpty(label))
            {
                label = drive.DriveType switch
                {
                    DriveType.Fixed => "Local Disk",
                    DriveType.Removable => "Removable Drive",
                    DriveType.Network => "Network Drive",
                    _ => "Local Disk"
                };
            }
            return new VolumeIdentity(
                FormatSerial(serial),
                label,
                drive.TotalSize,
                rootPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Searches all ready drives for a volume whose serial number matches <paramref name="serialNumber"/> (case-insensitive). Returns <c>null</c> if the volume is not currently mounted.</summary>
    public static VolumeIdentity? FindBySerial(string serialNumber)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var id = GetVolumeIdentity(drive.RootDirectory.FullName);
                if (id != null && string.Equals(id.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Returns identity information for every drive that is currently ready (mounted and readable).</summary>
    public static IReadOnlyList<VolumeIdentity> GetAllVolumes()
    {
        var result = new List<VolumeIdentity>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var id = GetVolumeIdentity(drive.RootDirectory.FullName);
            if (id != null) result.Add(id);
        }
        return result;
    }

    // Converts the raw 32-bit serial from GetVolumeInformation into the canonical "ABCD-1234" display form
    private static string FormatSerial(uint serial) =>
        $"{serial >> 16:X4}-{serial & 0xFFFF:X4}";
}
