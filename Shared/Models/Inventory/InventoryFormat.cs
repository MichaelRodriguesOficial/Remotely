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
}
