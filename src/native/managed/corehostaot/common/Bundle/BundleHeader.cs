// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Bundle header flags.
/// </summary>
[Flags]
public enum BundleFlags : ulong
{
    None = 0,

    /// <summary>
    /// .NET Core 3.x compatibility mode - extract all files.
    /// </summary>
    NetCoreApp3CompatMode = 1,
}

/// <summary>
/// Represents the bundle header.
/// </summary>
public sealed class BundleHeader
{
    /// <summary>
    /// Major version of the bundle format.
    /// </summary>
    public uint MajorVersion { get; init; }

    /// <summary>
    /// Minor version of the bundle format.
    /// </summary>
    public uint MinorVersion { get; init; }

    /// <summary>
    /// Number of files embedded in the bundle.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public string BundleId { get; init; } = "";

    /// <summary>
    /// Offset of deps.json within the bundle (v2+).
    /// </summary>
    public long DepsJsonOffset { get; init; }

    /// <summary>
    /// Size of deps.json within the bundle (v2+).
    /// </summary>
    public long DepsJsonSize { get; init; }

    /// <summary>
    /// Offset of runtimeconfig.json within the bundle (v2+).
    /// </summary>
    public long RuntimeConfigOffset { get; init; }

    /// <summary>
    /// Size of runtimeconfig.json within the bundle (v2+).
    /// </summary>
    public long RuntimeConfigSize { get; init; }

    /// <summary>
    /// Bundle flags (v2+).
    /// </summary>
    public BundleFlags Flags { get; init; }

    /// <summary>
    /// Whether to run in .NET Core 3.x compatibility mode (extract all files).
    /// </summary>
    public bool IsNetCoreApp3CompatMode => (Flags & BundleFlags.NetCoreApp3CompatMode) != 0;

    /// <summary>
    /// Whether deps.json is embedded and its location is known.
    /// </summary>
    public bool HasDepsJson => DepsJsonOffset != 0 && DepsJsonSize != 0;

    /// <summary>
    /// Whether runtimeconfig.json is embedded and its location is known.
    /// </summary>
    public bool HasRuntimeConfig => RuntimeConfigOffset != 0 && RuntimeConfigSize != 0;

    /// <summary>
    /// Reads a bundle header from the reader.
    /// </summary>
    internal static BundleHeader Read(BundleReader reader)
    {
        // Read fixed header
        uint majorVersion = reader.ReadUInt32();
        uint minorVersion = reader.ReadUInt32();
        int fileCount = reader.ReadInt32();

        // Validate version
        if (majorVersion < 1 || majorVersion > 6)
        {
            throw new InvalidOperationException($"Unsupported bundle version: {majorVersion}.{minorVersion}");
        }

        // Read bundle ID (7-bit encoded length-prefixed string)
        string bundleId = reader.ReadString();

        // Read v2+ header fields
        long depsJsonOffset = 0;
        long depsJsonSize = 0;
        long runtimeConfigOffset = 0;
        long runtimeConfigSize = 0;
        BundleFlags flags = BundleFlags.None;

        if (majorVersion >= 2)
        {
            depsJsonOffset = reader.ReadInt64();
            depsJsonSize = reader.ReadInt64();
            runtimeConfigOffset = reader.ReadInt64();
            runtimeConfigSize = reader.ReadInt64();
            flags = (BundleFlags)reader.ReadUInt64();
        }

        return new BundleHeader
        {
            MajorVersion = majorVersion,
            MinorVersion = minorVersion,
            FileCount = fileCount,
            BundleId = bundleId,
            DepsJsonOffset = depsJsonOffset,
            DepsJsonSize = depsJsonSize,
            RuntimeConfigOffset = runtimeConfigOffset,
            RuntimeConfigSize = runtimeConfigSize,
            Flags = flags,
        };
    }
}
