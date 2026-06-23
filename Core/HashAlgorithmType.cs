namespace HashCheck.Core;

/// <summary>Identifies the hashing algorithm stored in a <c>[META]</c> section. Enum ordinal matches the display index in <see cref="Hashing.HasherFactory"/>.</summary>
public enum HashAlgorithmType
{
    XxHash3,
    XxHash128,
    Crc64,
    Crc32,
    MD5,
    SHA1,
    SHA256
}
