// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Represents the bundle manifest containing all file entries.
/// </summary>
public sealed class BundleManifest
{
    /// <summary>
    /// The bundle header.
    /// </summary>
    public BundleHeader Header { get; }

    /// <summary>
    /// All file entries in the bundle.
    /// </summary>
    public IReadOnlyList<BundleFileEntry> Files { get; }

    /// <summary>
    /// Whether any files need extraction to disk.
    /// </summary>
    public bool FilesNeedExtraction { get; }

    private readonly Dictionary<string, BundleFileEntry> _filesByPath;

    private BundleManifest(BundleHeader header, List<BundleFileEntry> files)
    {
        Header = header;
        Files = files;

        _filesByPath = new Dictionary<string, BundleFileEntry>(System.StringComparer.OrdinalIgnoreCase);
        bool needsExtraction = false;

        foreach (var entry in files)
        {
            _filesByPath[entry.RelativePath] = entry;
            if (entry.NeedsExtraction)
            {
                needsExtraction = true;
            }
        }

        FilesNeedExtraction = needsExtraction || header.IsNetCoreApp3CompatMode;
    }

    /// <summary>
    /// Tries to find a file entry by relative path.
    /// </summary>
    public BundleFileEntry? FindFile(string relativePath)
    {
        // Normalize path separators
        relativePath = relativePath.Replace('\\', '/');
        return _filesByPath.TryGetValue(relativePath, out var entry) ? entry : null;
    }

    /// <summary>
    /// Reads the manifest from a bundle reader.
    /// </summary>
    internal static BundleManifest Read(BundleReader reader)
    {
        var header = BundleHeader.Read(reader);
        var files = new List<BundleFileEntry>(header.FileCount);

        for (int i = 0; i < header.FileCount; i++)
        {
            var entry = ReadFileEntry(reader, header.MajorVersion);
            files.Add(entry);
        }

        return new BundleManifest(header, files);
    }

    private static BundleFileEntry ReadFileEntry(BundleReader reader, uint bundleVersion)
    {
        long offset = reader.ReadInt64();
        long size = reader.ReadInt64();

        // CompressedSize added in v6
        long compressedSize = bundleVersion >= 6 ? reader.ReadInt64() : 0;

        var type = (BundleFileType)reader.ReadByte();
        string relativePath = reader.ReadString();

        return new BundleFileEntry
        {
            Offset = offset,
            Size = size,
            CompressedSize = compressedSize,
            Type = type,
            RelativePath = relativePath,
        };
    }
}
