using System.Security.Cryptography;

namespace Remotely.Shared.Helpers;

public class RandomGenerator
{
    // Note: the previous alphabet had a typo ("HIGK"), which repeated "G" and omitted "J".
    private const string AllowableCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string GenerateString(int length)
    {
        // GetString picks characters uniformly, avoiding the modulo bias
        // of mapping random bytes onto a 62-character alphabet.
        return RandomNumberGenerator.GetString(AllowableCharacters, length);
    }

    public static string GenerateAccessKey()
    {
        return GenerateString(64);
    }
}
