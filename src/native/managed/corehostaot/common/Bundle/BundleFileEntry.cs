// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Represents an entry in the bundle manifest.
/// </summary>
public sealed class BundleFileEntry
{
    /// <summary>
    /// Offset of the file data within the bundle.
    /// </summary>
    public long Offset { get; init; }

    /// <summary>
    /// Size of the file data (possibly compressed).
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Uncompressed size (0 if not compressed, or same as Size in v1-v5).
    /// </summary>
    public long CompressedSize { get; init; }

    /// <summary>
    /// Type of the embedded file.
    /// </summary>
    public BundleFileType Type { get; init; }

    /// <summary>
    /// Relative path of the file within the bundle.
    /// </summary>
    public string RelativePath { get; init; } = "";

    /// <summary>
    /// Whether this entry is compressed.
    /// </summary>
    public bool IsCompressed => CompressedSize > 0 && CompressedSize != Size;

    /// <summary>
    /// Whether this entry requires extraction to disk.
    /// </summary>
    public bool NeedsExtraction => Type == BundleFileType.NativeBinary;
}
