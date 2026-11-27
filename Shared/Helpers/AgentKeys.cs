using System.Security.Cryptography;
using System.Text;

namespace Remotely.Shared.Helpers;

/// <summary>Chave por computador: o agente gera e guarda; o servidor guarda só o hash.</summary>
public static class AgentKeys
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 em hexadecimal, ou nulo para chave vazia ou fora do formato.</summary>
    public static string? Hash(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 32 or > 200)
        {
            return null;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    public static bool Matches(string? storedHash, string? key)
    {
        var hash = Hash(key);
        if (string.IsNullOrEmpty(storedHash) || hash is null || storedHash.Length != hash.Length)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash),
            Encoding.ASCII.GetBytes(hash));
    }
}
