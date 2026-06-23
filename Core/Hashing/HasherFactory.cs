namespace HashCheck.Core.Hashing;

/// <summary>Creates <see cref="IHasher"/> instances and provides display metadata for the UI. Display index == <see cref="HashAlgorithmType"/> ordinal, so casting is safe.</summary>
public static class HasherFactory
{
    /// <summary>Returns a new <see cref="IHasher"/> for the specified algorithm.</summary>
    public static IHasher Create(HashAlgorithmType algorithm) => algorithm switch
    {
        HashAlgorithmType.XxHash3 => new XxHash3Hasher(),
        HashAlgorithmType.XxHash128 => new XxHash128Hasher(),
        HashAlgorithmType.Crc64 => new Crc64Hasher(),
        HashAlgorithmType.Crc32 => new Crc32Hasher(),
        HashAlgorithmType.MD5 => new Md5Hasher(),
        HashAlgorithmType.SHA1 => new Sha1Hasher(),
        HashAlgorithmType.SHA256 => new Sha256Hasher(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
    };

    /// <summary>Human-readable names aligned with <see cref="HashAlgorithmType"/> ordinal order; used to populate algorithm drop-downs.</summary>
    public static string[] AlgorithmDisplayNames =>
    [
        "XxHash3 (fastest, default)",
        "XxHash128",
        "CRC-64",
        "CRC-32",
        "MD5",
        "SHA-1",
        "SHA-256"
    ];

    /// <summary>Converts a UI combo-box index to the corresponding <see cref="HashAlgorithmType"/>.</summary>
    public static HashAlgorithmType FromDisplayIndex(int index) => (HashAlgorithmType)index;
    /// <summary>Converts a <see cref="HashAlgorithmType"/> to its UI combo-box index.</summary>
    public static int ToDisplayIndex(HashAlgorithmType algo) => (int)algo;
}
