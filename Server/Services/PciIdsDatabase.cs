using Remotely.Shared.Models.Inventory;
using System.Globalization;
using System.IO.Compression;

namespace Remotely.Server.Services;

/// <summary>
/// Nomes de fabricantes e dispositivos PCI (base pci.ids, a mesma que o GLPI usa),
/// para mostrar "Alder Lake PCH USB 3.2 xHCI Host Controller" no lugar do nome
/// genérico do Windows ("Intel(R) USB 3.10 eXtensible Host Controller - 1.20").
/// </summary>
public interface IPciIdsDatabase
{
    (string? Vendor, string? Device) Lookup(string? pciId);

    /// <summary>Preenche nome e fabricante dos componentes que têm <see cref="InventoryComponent.PciId"/>.</summary>
    void Apply(DeviceInventory inventory);
}

public sealed class PciIdsDatabase : IPciIdsDatabase
{
    private const string ResourceName = "Remotely.Server.Resources.pci.ids.gz";

    private readonly Lazy<Dictionary<ushort, (string Name, Dictionary<ushort, string> Devices)>> _data;
    private readonly ILogger<PciIdsDatabase> _logger;

    public PciIdsDatabase(ILogger<PciIdsDatabase> logger)
    {
        _logger = logger;
        _data = new Lazy<Dictionary<ushort, (string, Dictionary<ushort, string>)>>(Load, isThreadSafe: true);
    }

    public (string? Vendor, string? Device) Lookup(string? pciId)
    {
        if (string.IsNullOrWhiteSpace(pciId))
        {
            return (null, null);
        }

        var parts = pciId.Split(':');
        if (parts.Length != 2 ||
            !ushort.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendorId) ||
            !ushort.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var deviceId))
        {
            return (null, null);
        }

        if (!_data.Value.TryGetValue(vendorId, out var vendor))
        {
            return (null, null);
        }

        return (vendor.Name, vendor.Devices.TryGetValue(deviceId, out var device) ? device : null);
    }

    public void Apply(DeviceInventory inventory)
    {
        foreach (var component in inventory.Components)
        {
            if (string.IsNullOrWhiteSpace(component.PciId))
            {
                continue;
            }

            var (vendor, device) = Lookup(component.PciId);
            if (device is not null)
            {
                if (!string.IsNullOrWhiteSpace(component.Name) &&
                    !string.Equals(component.Name, device, StringComparison.OrdinalIgnoreCase))
                {
                    component.Details.TryAdd("Interface", component.Name);
                }
                component.Name = device;
            }

            if (vendor is not null)
            {
                component.Manufacturer = vendor;
            }
        }
    }

    private Dictionary<ushort, (string Name, Dictionary<ushort, string> Devices)> Load()
    {
        var result = new Dictionary<ushort, (string, Dictionary<ushort, string>)>();

        try
        {
            using var stream = OpenDatabase();
            if (stream is null)
            {
                _logger.LogWarning("pci.ids not found. PCI devices will keep the names reported by the agents.");
                return result;
            }

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            Dictionary<ushort, string>? currentDevices = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length < 6 || line[0] == '#')
                {
                    continue;
                }

                if (line[0] != '\t')
                {
                    if (ushort.TryParse(line.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendorId))
                    {
                        currentDevices = [];
                        result[vendorId] = (line[4..].Trim(), currentDevices);
                    }
                    else
                    {
                        currentDevices = null;
                    }
                    continue;
                }

                if (currentDevices is not null &&
                    line.Length > 6 &&
                    line[1] != '\t' &&
                    ushort.TryParse(line.AsSpan(1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var deviceId))
                {
                    currentDevices[deviceId] = line[5..].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading pci.ids.");
        }

        return result;
    }

    private static Stream? OpenDatabase()
    {
        var embedded = typeof(PciIdsDatabase).Assembly.GetManifestResourceStream(ResourceName);
        if (embedded is not null)
        {
            return embedded;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "pci.ids.gz");
        return File.Exists(path) ? File.OpenRead(path) : null;
    }
}
