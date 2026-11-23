using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Remotely.Agent.Services.Inventory;

/// <summary>
/// Leitor da tabela SMBIOS (a mesma fonte do dmidecode, que o GLPI Agent usa).
/// No Windows a tabela vem de MSSmBios_RawSMBiosTables (WMI) e no Linux de
/// /sys/firmware/dmi/tables/DMI.
/// </summary>
public sealed class SmbiosReader
{
    public sealed class Structure
    {
        public required byte Type { get; init; }
        public required byte[] Data { get; init; }
        public required List<string> Strings { get; init; }

        public byte? Byte(int offset) => offset < Data.Length ? Data[offset] : null;

        public ushort? Word(int offset) =>
            offset + 1 < Data.Length ? (ushort)(Data[offset] | (Data[offset + 1] << 8)) : null;

        public uint? DWord(int offset) =>
            offset + 3 < Data.Length
                ? (uint)(Data[offset] | (Data[offset + 1] << 8) | (Data[offset + 2] << 16) | (Data[offset + 3] << 24))
                : null;

        /// <summary>Texto referenciado pelo byte em <paramref name="offset"/> (índice a partir de 1).</summary>
        public string? String(int offset)
        {
            var index = Byte(offset);
            if (index is null or 0 || index.Value > Strings.Count)
            {
                return null;
            }
            var value = Strings[index.Value - 1].Trim();
            return value.Length == 0 ? null : value;
        }
    }

    public List<Structure> Structures { get; } = [];

    public IEnumerable<Structure> OfType(byte type) => Structures.Where(x => x.Type == type);

    public static SmbiosReader Parse(byte[] table)
    {
        var reader = new SmbiosReader();
        var offset = 0;

        while (offset + 4 <= table.Length)
        {
            var type = table[offset];
            var length = table[offset + 1];
            if (length < 4 || offset + length > table.Length)
            {
                break;
            }

            var data = table.AsSpan(offset, length).ToArray();

            // Strings: uma após a outra, cada uma terminada em 0; a seção termina com 00 00.
            var strings = new List<string>();
            var position = offset + length;
            var start = position;
            while (position + 1 < table.Length)
            {
                if (table[position] == 0)
                {
                    if (position > start)
                    {
                        strings.Add(Encoding.ASCII.GetString(table, start, position - start));
                    }

                    if (table[position + 1] == 0)
                    {
                        position += 2;
                        break;
                    }

                    position++;
                    start = position;
                    continue;
                }
                position++;
            }

            reader.Structures.Add(new Structure { Type = type, Data = data, Strings = strings });

            // Tipo 127 = fim da tabela.
            if (type == 127)
            {
                break;
            }

            offset = position;
        }

        return reader;
    }

    // -----------------------------------------------------------------
    // Interpretação dos tipos usados no inventário
    // -----------------------------------------------------------------

    public sealed record Processor(
        string? Socket,
        string? Manufacturer,
        string? Version,
        int? MaxSpeedMhz,
        int? CurrentSpeedMhz,
        int? Cores,
        int? Threads,
        string? SerialNumber,
        string? PartNumber);

    public IEnumerable<Processor> GetProcessors()
    {
        foreach (var s in OfType(4))
        {
            // Status (0x18): bit 6 = soquete ocupado.
            var status = s.Byte(0x18);
            if (status is not null && (status.Value & 0x40) == 0)
            {
                continue;
            }

            int? cores = s.Byte(0x23);
            if (cores == 0xFF)
            {
                cores = s.Word(0x2A);
            }
            int? threads = s.Byte(0x25);
            if (threads == 0xFF)
            {
                threads = s.Word(0x2E);
            }

            yield return new Processor(
                s.String(0x04),
                s.String(0x07),
                s.String(0x10),
                NonZero(s.Word(0x14)),
                NonZero(s.Word(0x16)),
                NonZero(cores),
                NonZero(threads),
                s.String(0x20),
                s.String(0x22));
        }
    }

    public sealed record MemoryDevice(
        string? Locator,
        string? BankLocator,
        long SizeBytes,
        string? FormFactor,
        string? Type,
        int? SpeedMhz,
        int? ConfiguredSpeedMhz,
        string? Manufacturer,
        string? SerialNumber,
        string? PartNumber);

    public IEnumerable<MemoryDevice> GetMemoryDevices()
    {
        foreach (var s in OfType(17))
        {
            var size = s.Word(0x0C);
            if (size is null or 0 or 0xFFFF)
            {
                // Slot vazio ou tamanho desconhecido.
                continue;
            }

            long bytes;
            if (size == 0x7FFF)
            {
                bytes = (long)(s.DWord(0x1C) ?? 0) * 1024 * 1024;
            }
            else if ((size.Value & 0x8000) != 0)
            {
                bytes = (long)(size.Value & 0x7FFF) * 1024;
            }
            else
            {
                bytes = (long)size.Value * 1024 * 1024;
            }

            int? speed = s.Word(0x15);
            if (speed == 0xFFFF)
            {
                speed = (int?)s.DWord(0x54);
            }
            int? configured = s.Word(0x20);
            if (configured == 0xFFFF)
            {
                configured = (int?)s.DWord(0x58);
            }

            yield return new MemoryDevice(
                s.String(0x10),
                s.String(0x11),
                bytes,
                MemoryFormFactor(s.Byte(0x0E)),
                MemoryType(s.Byte(0x12)),
                NonZero(speed),
                NonZero(configured),
                s.String(0x17),
                s.String(0x18),
                s.String(0x1A));
        }
    }

    public sealed record Battery(
        string? Location,
        string? Manufacturer,
        string? ManufactureDate,
        string? SerialNumber,
        string? Name,
        string? Chemistry,
        int? DesignCapacityMwh,
        int? DesignVoltageMv);

    public IEnumerable<Battery> GetBatteries()
    {
        foreach (var s in OfType(22))
        {
            var multiplier = s.Byte(0x15) is { } m and > 0 ? m : 1;
            var capacity = s.Word(0x0A);

            var serial = s.String(0x07);
            if (serial is null && s.Word(0x10) is { } sbdsSerial and > 0)
            {
                serial = sbdsSerial.ToString();
            }

            var date = s.String(0x06);
            if (date is null && s.Word(0x12) is { } packed and > 0)
            {
                // Bits 15:9 ano (desde 1980), 8:5 mês, 4:0 dia.
                var year = 1980 + (packed >> 9);
                var month = (packed >> 5) & 0x0F;
                var day = packed & 0x1F;
                if (month is >= 1 and <= 12 && day is >= 1 and <= 31)
                {
                    date = $"{year:0000}-{month:00}-{day:00}";
                }
            }

            var chemistry = s.Byte(0x09) switch
            {
                3 => "Chumbo-ácido",
                4 => "NiCd",
                5 => "NiMH",
                6 => "Li-ion",
                7 => "Zinco-ar",
                8 => "LiP",
                _ => null
            } ?? s.String(0x14);

            yield return new Battery(
                s.String(0x04),
                s.String(0x05),
                date,
                serial,
                s.String(0x08),
                chemistry,
                capacity is null or 0 ? null : capacity * multiplier,
                NonZero(s.Word(0x0C)));
        }
    }

    private static int? NonZero(int? value) => value is null or 0 ? null : value;

    private static string? MemoryFormFactor(byte? value) => value switch
    {
        0x09 => "DIMM",
        0x0D => "SODIMM",
        0x0B => "RIMM",
        0x0F => "FB-DIMM",
        0x10 => "Die",
        0x0E => "SRIMM",
        _ => null
    };

    private static string? MemoryType(byte? value) => value switch
    {
        0x12 => "DDR",
        0x13 => "DDR2",
        0x14 => "DDR2 FB-DIMM",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x20 => "HBM",
        0x21 => "HBM2",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        0x24 => "HBM3",
        _ => null
    };
}
