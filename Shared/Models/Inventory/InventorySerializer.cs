using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remotely.Shared.Models.Inventory;

/// <summary>
/// Serializa o inventário como JSON compactado com gzip. A lista de softwares
/// costuma ter centenas de itens; compactada ela cabe com folga numa mensagem
/// do SignalR.
/// </summary>
public static class InventorySerializer
{
    /// <summary>Limite do JSON descompactado (proteção contra "zip bomb").</summary>
    public const int MaxJsonBytes = 8 * 1024 * 1024;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string ToJson(DeviceInventory inventory) =>
        JsonSerializer.Serialize(inventory, JsonOptions);

    public static DeviceInventory? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeviceInventory>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static byte[] Compress(DeviceInventory inventory)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(json, 0, json.Length);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Descompacta e valida. Retorna falso se os dados estiverem corrompidos,
    /// grandes demais ou não forem um inventário.
    /// </summary>
    public static bool TryDecompress(byte[]? data, out DeviceInventory? inventory, out string json)
    {
        inventory = null;
        json = string.Empty;

        if (data is null || data.Length == 0)
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxJsonBytes)
                {
                    return false;
                }
                output.Write(buffer, 0, read);
            }

            inventory = JsonSerializer.Deserialize<DeviceInventory>(output.ToArray(), JsonOptions);
            if (inventory is null)
            {
                return false;
            }

            // Regrava com as opções do servidor (normaliza e remove campos desconhecidos).
            json = ToJson(inventory);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return false;
        }
    }
}
