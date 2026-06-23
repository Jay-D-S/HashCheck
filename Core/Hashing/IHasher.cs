namespace HashCheck.Core.Hashing;

/// <summary>Abstraction over a content-hashing algorithm. Returns a lower-case hex string.</summary>
public interface IHasher
{
    /// <summary>The algorithm this hasher implements.</summary>
    HashAlgorithmType Algorithm { get; }
    /// <summary>Reads <paramref name="stream"/> to completion and returns a lower-case hex digest. Reports each chunk's byte count via <paramref name="bytesProgress"/>.</summary>
    Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct);
}
