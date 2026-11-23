#nullable enable

using MessagePack;
using Remotely.Shared.Models;
using Remotely.Shared.Primitives;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Remotely.Shared.Services;

public interface IEmbeddedServerDataProvider
{
    string GetEncodedFileName(string filePath, EmbeddedServerData serverData);

    /// <summary>
    /// Nome de arquivo curto e legível, por exemplo "Remotely_Desktop_x64 (suporte.empresa.com.br).exe".
    /// Só é possível quando não há organização específica (usa a padrão do servidor);
    /// caso contrário volta para o formato com os dados codificados entre colchetes.
    /// </summary>
    string GetFriendlyFileName(string displayName, string extension, EmbeddedServerData serverData);

    /// <summary>
    /// Bloco com o endereço do servidor e a organização, anexado ao final do executável
    /// do cliente. Assim o arquivo pode ter um nome simples ("Remotely_Desktop_x64.exe").
    /// </summary>
    byte[] CreateTrailer(EmbeddedServerData serverData);

    /// <summary>
    /// Lê os dados do servidor: primeiro do final do executável, depois do nome do
    /// arquivo (formatos "(servidor)" e "[dados]" das versões anteriores).
    /// </summary>
    Result<EmbeddedServerData> TryGetEmbeddedData(string filePath);
}

public class EmbeddedServerDataProvider : IEmbeddedServerDataProvider
{
    public static EmbeddedServerDataProvider Instance { get; } = new();

    // Formato do final do arquivo: [dados MessagePack][tamanho int32 LE][marcador de 8 bytes].
    private static readonly byte[] _trailerMagic = Encoding.ASCII.GetBytes("RMTLYSD1");
    private const int TrailerFooterLength = 12;
    private const int MaxTrailerPayload = 16 * 1024;

    // (host), (host_porta), (http~host) ou (http~host_porta)
    private static readonly Regex _friendlyTokenRegex = new(
        @"\((?<http>http~)?(?<host>[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?)(?:_(?<port>\d{1,5}))?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string GetFriendlyFileName(string displayName, string extension, EmbeddedServerData serverData)
    {
        var url = serverData.ServerUrl;
        var canUseFriendlyName =
            string.IsNullOrWhiteSpace(serverData.OrganizationId) &&
            (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) &&
            (url.HostNameType == UriHostNameType.Dns || url.HostNameType == UriHostNameType.IPv4) &&
            (url.AbsolutePath == "/" || string.IsNullOrEmpty(url.AbsolutePath));

        if (!canUseFriendlyName)
        {
            return GetEncodedFileName($"{displayName}{extension}", serverData);
        }

        var token = url.Host;
        if (!url.IsDefaultPort)
        {
            token += $"_{url.Port}";
        }
        if (url.Scheme == Uri.UriSchemeHttp)
        {
            token = $"http~{token}";
        }

        return $"{displayName} ({token}){extension}";
    }

    public string GetEncodedFileName(string filePath, EmbeddedServerData serverData)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);

        // Make the base64 string safe file paths and URIs.
        var encodedData = Convert
            .ToBase64String(MessagePackSerializer.Serialize(serverData))
            .Replace("/", "_")
            .Replace("+", "-");

        return $"{fileName}[{encodedData}]{ext}";
    }

    public byte[] CreateTrailer(EmbeddedServerData serverData)
    {
        var payload = MessagePackSerializer.Serialize(serverData);
        var trailer = new byte[payload.Length + TrailerFooterLength];
        payload.CopyTo(trailer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(trailer.AsSpan(payload.Length, 4), payload.Length);
        _trailerMagic.CopyTo(trailer, payload.Length + 4);
        return trailer;
    }

    public Result<EmbeddedServerData> TryGetEmbeddedData(string filePath)
    {
        if (TryReadTrailer(filePath, out var trailerData))
        {
            return Result.Ok(trailerData);
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (!fileName.Contains('[') && TryGetFriendlyData(fileName, out var friendlyData))
        {
            return Result.Ok(friendlyData);
        }

        try
        {
            var start = fileName.LastIndexOf('[') + 1;
            var end = fileName.LastIndexOf(']');
            var base64 = fileName[start..end]
                .Replace("_", "/")
                .Replace("-", "+");

            var embeddedData = MessagePackSerializer.Deserialize<EmbeddedServerData>(Convert.FromBase64String(base64));
            return Result.Ok(embeddedData);
        }
        catch (Exception ex)
        {
            return Result.Fail<EmbeddedServerData>(ex);
        }
    }

    private static bool TryReadTrailer(string filePath, out EmbeddedServerData data)
    {
        data = null!;

        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < TrailerFooterLength + 1)
            {
                return false;
            }

            var footer = new byte[TrailerFooterLength];
            stream.Seek(-TrailerFooterLength, SeekOrigin.End);
            stream.ReadExactly(footer);

            if (!footer.AsSpan(4).SequenceEqual(_trailerMagic))
            {
                return false;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(footer.AsSpan(0, 4));
            if (length <= 0 || length > MaxTrailerPayload || length > stream.Length - TrailerFooterLength)
            {
                return false;
            }

            var payload = new byte[length];
            stream.Seek(-(TrailerFooterLength + length), SeekOrigin.End);
            stream.ReadExactly(payload);

            var result = MessagePackSerializer.Deserialize<EmbeddedServerData>(payload);
            if (result?.ServerUrl is null)
            {
                return false;
            }

            data = result;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetFriendlyData(string fileName, out EmbeddedServerData data)
    {
        data = null!;

        // O navegador pode acrescentar " (1)" em downloads repetidos; usa o último
        // trecho entre parênteses que pareça um endereço de servidor.
        var matches = _friendlyTokenRegex.Matches(fileName);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var host = match.Groups["host"].Value;
            if (!host.Contains('.') && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scheme = match.Groups["http"].Success ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;
            var port = match.Groups["port"].Success ? $":{match.Groups["port"].Value}" : string.Empty;

            if (Uri.TryCreate($"{scheme}://{host}{port}", UriKind.Absolute, out var uri))
            {
                data = new EmbeddedServerData(uri, null);
                return true;
            }
        }

        return false;
    }
}
