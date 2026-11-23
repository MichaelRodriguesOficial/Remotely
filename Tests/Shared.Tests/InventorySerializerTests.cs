using Remotely.Shared.Models.Inventory;
using System.IO.Compression;
using System.Text;

namespace Remotely.Shared.Tests;

[TestClass]
public class InventorySerializerTests
{
    [TestMethod]
    public void Compress_RoundTrips()
    {
        var inventory = new DeviceInventory
        {
            Uuid = "4C4C4544-004A-5110-8058-B7C04F563833",
            DeviceType = "Notebook",
            Manufacturer = "Dell Inc.",
            SerialNumber = "7JQX8V3",
            OperatingSystem = new InventoryOperatingSystem { Name = "Microsoft Windows 11 Pro", SerialNumber = "00330-80000-00000-AA123" },
            Software = Enumerable.Range(0, 500)
                .Select(i => new InventorySoftware { Name = $"Programa {i}", Version = "1.0", Publisher = "Fornecedor" })
                .ToList(),
            NetworkPorts = [new InventoryNetworkPort { Name = "Ethernet", MacAddress = "B0:4F:13:2A:91:C4", IPv4 = ["10.0.0.5"] }]
        };

        var data = InventorySerializer.Compress(inventory);

        Assert.IsTrue(InventorySerializer.TryDecompress(data, out var result, out var json));
        Assert.IsNotNull(result);
        Assert.AreEqual("7JQX8V3", result.SerialNumber);
        Assert.AreEqual("00330-80000-00000-AA123", result.OperatingSystem?.SerialNumber);
        Assert.AreEqual(500, result.Software.Count);
        Assert.AreEqual("10.0.0.5", result.NetworkPorts[0].IPv4[0]);
        Assert.AreEqual("Notebook", InventorySerializer.FromJson(json)?.DeviceType);
    }

    [TestMethod]
    public void TryDecompress_RejectsGarbage()
    {
        Assert.IsFalse(InventorySerializer.TryDecompress(null, out _, out _));
        Assert.IsFalse(InventorySerializer.TryDecompress([1, 2, 3, 4], out _, out _));
    }

    [TestMethod]
    public void TryDecompress_RejectsOversizedPayload()
    {
        // JSON válido, mas maior que o limite depois de descompactado.
        var json = "{\"model\":\"" + new string('a', InventorySerializer.MaxJsonBytes + 10) + "\"}";
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes, 0, bytes.Length);
        }

        Assert.IsFalse(InventorySerializer.TryDecompress(output.ToArray(), out _, out _));
    }

    [TestMethod]
    [DataRow(0L, "")]
    [DataRow(512L, "512 B")]
    [DataRow(8589934592L, "8 GB")]
    [DataRow(80530636800L, "75 GB")]
    [DataRow(1610612736L, "1,5 GB")]
    public void Bytes_FormatsWithDecimalComma(long bytes, string expected)
    {
        Assert.AreEqual(expected, InventoryFormat.Bytes(bytes));
    }
}
