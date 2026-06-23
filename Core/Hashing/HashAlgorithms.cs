using System.IO.Hashing;
using System.Security.Cryptography;

namespace HashCheck.Core.Hashing;

/// <summary>IHasher implementation using XxHash3 (64-bit non-cryptographic; fastest option, default).</summary>
public sealed class XxHash3Hasher : IHasher
{
    public HashAlgorithmType Algorithm => HashAlgorithmType.XxHash3;

    public async Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        var hasher = new XxHash3();
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.Append(buffer.AsSpan(0, read));
            bytesProgress?.Report(read);
        }
        return Convert.ToHexString(hasher.GetCurrentHash()).ToLowerInvariant();
    }
}

/// <summary>IHasher implementation using XxHash128 (128-bit non-cryptographic).</summary>
public sealed class XxHash128Hasher : IHasher
{
    public HashAlgorithmType Algorithm => HashAlgorithmType.XxHash128;

    public async Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        var hasher = new XxHash128();
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.Append(buffer.AsSpan(0, read));
            bytesProgress?.Report(read);
        }
        return Convert.ToHexString(hasher.GetCurrentHash()).ToLowerInvariant();
    }
}

/// <summary>IHasher implementation using CRC-64.</summary>
public sealed class Crc64Hasher : IHasher
{
    public HashAlgorithmType Algorithm => HashAlgorithmType.Crc64;

    public async Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        var hasher = new Crc64();
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.Append(buffer.AsSpan(0, read));
            bytesProgress?.Report(read);
        }
        return Convert.ToHexString(hasher.GetCurrentHash()).ToLowerInvariant();
    }
}

/// <summary>IHasher implementation using CRC-32.</summary>
public sealed class Crc32Hasher : IHasher
{
    public HashAlgorithmType Algorithm => HashAlgorithmType.Crc32;

    public async Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        var hasher = new Crc32();
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.Append(buffer.AsSpan(0, read));
            bytesProgress?.Report(read);
        }
        return Convert.ToHexString(hasher.GetCurrentHash()).ToLowerInvariant();
    }
}

/// <summary>IHasher implementation using MD5 via <see cref="System.Security.Cryptography.IncrementalHash"/>.</summary>
public sealed class Md5Hasher : IHasher
{
    public HashAlgorithmType Algorithm => HashAlgorithmType.MD5;

    public async Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            bytesProgress?.Report(read);
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}

/// <summary>IHasher implementation using SHA-1 via <see cref="System.Security.Cryptography.IncrementalHash"/>.</summary>
public sealed class Sha1Hasher : IHasher
{
    public HashAlgorithmType Algorithm => HashAlgorithmType.SHA1;

    public async Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            bytesProgress?.Report(read);
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}

/// <summary>IHasher implementation using SHA-256 via <see cref="System.Security.Cryptography.IncrementalHash"/>.</summary>
public sealed class Sha256Hasher : IHasher
{
    public HashAlgorithmType Algorithm => HashAlgorithmType.SHA256;

    public async Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            bytesProgress?.Report(read);
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}
