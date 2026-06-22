using System.Runtime.InteropServices;
using System.Text;

namespace HashCheck.Core.Volumes;

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
                    DriveType.Fixed    => "Local Disk",
                    DriveType.Removable => "Removable Drive",
                    DriveType.Network  => "Network Drive",
                    _                  => "Local Disk"
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

    private static string FormatSerial(uint serial) =>
        $"{serial >> 16:X4}-{serial & 0xFFFF:X4}";
}
