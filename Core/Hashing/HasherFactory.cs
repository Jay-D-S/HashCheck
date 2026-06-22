namespace HashCheck.Core.Hashing;

public static class HasherFactory
{
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

    public static HashAlgorithmType FromDisplayIndex(int index) => (HashAlgorithmType)index;
    public static int ToDisplayIndex(HashAlgorithmType algo) => (int)algo;
}
