using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Remotely.Agent.Services.Diagnostics;

/// <summary>
/// Lê o SMART direto do disco no Windows, sem depender do que o fabricante expõe pelo WMI.
/// NVMe: IOCTL_STORAGE_QUERY_PROPERTY com StorageDeviceProtocolSpecificProperty (log 02h).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsSmart
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int PropertyStandardQuery = 0;
    private const int ProtocolTypeNvme = 3;
    private const int NVMeDataTypeLogPage = 2;
    private const int NvmeLogPageHealthInfo = 2;
    private const int NvmeLogSize = 512;
    private const int ProtocolSpecificDataSize = 40;

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;

    /// <summary>Lê o log "SMART / Health Information" (512 bytes) do NVMe \\.\PhysicalDriveN.</summary>
    public static byte[]? TryReadNvmeHealthLog(int diskIndex)
    {
        try
        {
            using var handle = Open(diskIndex);
            if (handle is null)
            {
                return null;
            }

            // STORAGE_PROPERTY_QUERY (PropertyId, QueryType) + STORAGE_PROTOCOL_SPECIFIC_DATA + espaço do log.
            var buffer = new byte[8 + ProtocolSpecificDataSize + NvmeLogSize];
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span[0..], StorageDeviceProtocolSpecificProperty);
            BinaryPrimitives.WriteInt32LittleEndian(span[4..], PropertyStandardQuery);
            var protocol = span[8..];
            BinaryPrimitives.WriteInt32LittleEndian(protocol[0..], ProtocolTypeNvme);
            BinaryPrimitives.WriteInt32LittleEndian(protocol[4..], NVMeDataTypeLogPage);
            BinaryPrimitives.WriteInt32LittleEndian(protocol[8..], NvmeLogPageHealthInfo);
            BinaryPrimitives.WriteInt32LittleEndian(protocol[12..], 0);
            BinaryPrimitives.WriteInt32LittleEndian(protocol[16..], ProtocolSpecificDataSize);
            BinaryPrimitives.WriteInt32LittleEndian(protocol[20..], NvmeLogSize);

            if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, buffer, buffer.Length, buffer, buffer.Length, out var returned, nint.Zero) ||
                returned < 8 + ProtocolSpecificDataSize)
            {
                return null;
            }

            // Saída: STORAGE_PROTOCOL_DATA_DESCRIPTOR (Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA).
            var descriptorProtocol = buffer.AsSpan(8);
            var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(descriptorProtocol[16..]);
            var dataLength = BinaryPrimitives.ReadInt32LittleEndian(descriptorProtocol[20..]);
            var start = 8 + dataOffset;
            if (dataOffset < ProtocolSpecificDataSize || dataLength < 200 || start + Math.Min(dataLength, NvmeLogSize) > returned)
            {
                return null;
            }

            return buffer.AsSpan(start, Math.Min(dataLength, NvmeLogSize)).ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static SafeFileHandle? Open(int diskIndex)
    {
        var handle = CreateFile($@"\\.\PhysicalDrive{diskIndex}", GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nint.Zero, OPEN_EXISTING, 0, nint.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, byte[] inBuffer, int inBufferSize,
        byte[] outBuffer, int outBufferSize, out int bytesReturned, nint overlapped);
}
