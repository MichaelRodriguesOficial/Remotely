using System.Globalization;

namespace Remotely.Shared.Models.Inventory;

public static class InventoryFormat
{
    /// <summary>"16 GB", "512 MB"... (base 1024).</summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        // Vírgula decimal sem depender de cultura instalada (contêineres usam modo invariante).
        return value.ToString(value >= 100 || unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " " + units[unit];
    }

    /// <summary>
    /// Esconde a chave do produto, deixando só o último bloco ("•••••-•••••-•••••-•••••-H9M7D").
    /// Usado para quem não é administrador.
    /// </summary>
    public static string? MaskProductKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var parts = key.Split('-');
        if (parts.Length < 2)
        {
            return new string('•', Math.Max(0, key.Length - 4)) + key[^Math.Min(4, key.Length)..];
        }

        return string.Join('-', parts.Select((part, index) => index == parts.Length - 1 ? part : new string('•', part.Length)));
    }
}
