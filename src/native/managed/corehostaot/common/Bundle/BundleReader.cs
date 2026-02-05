// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Reads data from a memory-mapped bundle file.
/// </summary>
internal sealed class BundleReader : IDisposable
{
    private readonly MemoryMappedFile _mappedFile;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _baseOffset;
    private readonly long _length;
    private long _position;

    public BundleReader(string bundlePath, long headerOffset)
    {
        _mappedFile = MemoryMappedFile.CreateFromFile(bundlePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _length = new FileInfo(bundlePath).Length;

        // Handle macOS universal binary (FAT) container
        _baseOffset = GetBaseOffset();
        _position = headerOffset;
    }

    private long GetBaseOffset()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return 0;
        }

        // Check for FAT magic number
        uint magic = ReadUInt32At(0);
        if (magic == 0xCAFEBABE) // FAT_MAGIC (big-endian)
        {
            // FAT 32-bit
            uint nfatArch = SwapEndian(ReadUInt32At(4));
            return FindArchOffsetInFat(8, nfatArch, is64Bit: false);
        }
        else if (magic == 0xCAFEBABF) // FAT_MAGIC_64 (big-endian)
        {
            // FAT 64-bit
            uint nfatArch = SwapEndian(ReadUInt32At(4));
            return FindArchOffsetInFat(8, nfatArch, is64Bit: true);
        }

        return 0;
    }

    private long FindArchOffsetInFat(long listOffset, uint count, bool is64Bit)
    {
        int cpuType = GetCurrentCpuType();
        int entrySize = is64Bit ? 32 : 20;

        for (uint i = 0; i < count; i++)
        {
            long entryOffset = listOffset + (i * entrySize);
            int archCpuType = (int)SwapEndian(ReadUInt32At(entryOffset));

            if (archCpuType == cpuType)
            {
                if (is64Bit)
                {
                    return (long)SwapEndian(ReadUInt64At(entryOffset + 8));
                }
                else
                {
                    return SwapEndian(ReadUInt32At(entryOffset + 8));
                }
            }
        }

        return 0;
    }

    private static int GetCurrentCpuType()
    {
        // Mach-O CPU types
        const int CPU_TYPE_X86_64 = 0x01000007;
        const int CPU_TYPE_ARM64 = 0x0100000C;

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => CPU_TYPE_X86_64,
            Architecture.Arm64 => CPU_TYPE_ARM64,
            _ => CPU_TYPE_X86_64,
        };
    }

    private static uint SwapEndian(uint value)
    {
        return ((value & 0x000000FF) << 24) |
               ((value & 0x0000FF00) << 8) |
               ((value & 0x00FF0000) >> 8) |
               ((value & 0xFF000000) >> 24);
    }

    private static ulong SwapEndian(ulong value)
    {
        return ((value & 0x00000000000000FF) << 56) |
               ((value & 0x000000000000FF00) << 40) |
               ((value & 0x0000000000FF0000) << 24) |
               ((value & 0x00000000FF000000) << 8) |
               ((value & 0x000000FF00000000) >> 8) |
               ((value & 0x0000FF0000000000) >> 24) |
               ((value & 0x00FF000000000000) >> 40) |
               ((value & 0xFF00000000000000) >> 56);
    }

    private uint ReadUInt32At(long offset)
    {
        return _accessor.ReadUInt32(offset);
    }

    private ulong ReadUInt64At(long offset)
    {
        return _accessor.ReadUInt64(offset);
    }

    public long Position
    {
        get => _position;
        set => _position = value;
    }

    public byte ReadByte()
    {
        byte value = _accessor.ReadByte(_baseOffset + _position);
        _position++;
        return value;
    }

    public int ReadInt32()
    {
        int value = _accessor.ReadInt32(_baseOffset + _position);
        _position += 4;
        return value;
    }

    public uint ReadUInt32()
    {
        uint value = _accessor.ReadUInt32(_baseOffset + _position);
        _position += 4;
        return value;
    }

    public long ReadInt64()
    {
        long value = _accessor.ReadInt64(_baseOffset + _position);
        _position += 8;
        return value;
    }

    public ulong ReadUInt64()
    {
        ulong value = _accessor.ReadUInt64(_baseOffset + _position);
        _position += 8;
        return value;
    }

    /// <summary>
    /// Reads a 7-bit encoded integer (used for string length prefix).
    /// </summary>
    public int Read7BitEncodedInt()
    {
        int result = 0;
        int shift = 0;
        byte b;

        do
        {
            if (shift >= 35)
            {
                throw new InvalidOperationException("Invalid 7-bit encoded integer");
            }

            b = ReadByte();
            result |= (b & 0x7F) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);

        return result;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string.
    /// </summary>
    public string ReadString()
    {
        int length = Read7BitEncodedInt();
        if (length == 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[length];
        ReadBytes(bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    public void ReadBytes(byte[] buffer, int offset, int count)
    {
        _accessor.ReadArray(_baseOffset + _position, buffer, offset, count);
        _position += count;
    }

    /// <summary>
    /// Reads bytes at a specific offset without changing position.
    /// </summary>
    public byte[] ReadBytesAt(long offset, int count)
    {
        byte[] buffer = new byte[count];
        _accessor.ReadArray(_baseOffset + offset, buffer, 0, count);
        return buffer;
    }

    /// <summary>
    /// Creates a stream for reading a section of the bundle.
    /// </summary>
    public Stream CreateStream(long offset, long length)
    {
        return _mappedFile.CreateViewStream(_baseOffset + offset, length, MemoryMappedFileAccess.Read);
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _mappedFile.Dispose();
    }
}
