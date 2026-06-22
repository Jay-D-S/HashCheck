namespace HashCheck.Core.Hashing;

public interface IHasher
{
    HashAlgorithmType Algorithm { get; }
    Task<string> ComputeHashAsync(Stream stream, IProgress<long>? bytesProgress, CancellationToken ct);
}
