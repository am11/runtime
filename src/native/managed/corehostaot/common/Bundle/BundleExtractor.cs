// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Manages extraction of files from a single-file bundle.
/// </summary>
public sealed class BundleExtractor : IDisposable
{
    private readonly string _bundlePath;
    private readonly BundleReader _reader;
    private readonly BundleManifest _manifest;
    private readonly string _extractionDir;
    private readonly HashSet<string> _extractedFiles = new(StringComparer.OrdinalIgnoreCase);

    public BundleManifest Manifest => _manifest;
    public string ExtractionDirectory => _extractionDir;

    private BundleExtractor(string bundlePath, BundleReader reader, BundleManifest manifest, string extractionDir)
    {
        _bundlePath = bundlePath;
        _reader = reader;
        _manifest = manifest;
        _extractionDir = extractionDir;
    }

    /// <summary>
    /// Opens a bundle from a single-file executable.
    /// </summary>
    public static BundleExtractor? Open(string bundlePath, long headerOffset)
    {
        if (headerOffset == 0)
        {
            return null;
        }

        try
        {
            var reader = new BundleReader(bundlePath, headerOffset);
            var manifest = BundleManifest.Read(reader);

            // Determine extraction directory
            string extractionDir = GetExtractionDirectory(bundlePath, manifest.Header.BundleId);

            return new BundleExtractor(bundlePath, reader, manifest, extractionDir);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the bundle header offset from the bundle marker at the end of the file.
    /// </summary>
    public static long GetBundleHeaderOffset(string bundlePath)
    {
        // The bundle marker is at the end of the file
        // It consists of:
        // - 8 bytes: header offset (int64)
        // - 32 bytes: signature

        const int MarkerSize = 40;
        byte[] expectedSignature =
        [
            // .net core bundle
            0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
            0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
            0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
            0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
        ];

        try
        {
            using var fs = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < MarkerSize)
            {
                return 0;
            }

            fs.Seek(-MarkerSize, SeekOrigin.End);

            byte[] marker = new byte[MarkerSize];
            if (fs.Read(marker, 0, MarkerSize) != MarkerSize)
            {
                return 0;
            }

            // Check signature
            for (int i = 0; i < 32; i++)
            {
                if (marker[8 + i] != expectedSignature[i])
                {
                    return 0;
                }
            }

            // Read header offset
            return BitConverter.ToInt64(marker, 0);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Checks if a file is a single-file bundle.
    /// </summary>
    public static bool IsBundle(string path)
    {
        return GetBundleHeaderOffset(path) != 0;
    }

    private static string GetExtractionDirectory(string bundlePath, string bundleId)
    {
        // Get the file name for the extraction directory
        string fileName = Path.GetFileNameWithoutExtension(bundlePath);

        // Check for override
        string? extractionRoot = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        if (string.IsNullOrEmpty(extractionRoot))
        {
            extractionRoot = Path.Combine(Path.GetTempPath(), ".net");
        }

        // Create a unique directory based on bundle ID and file hash
        string uniqueDir = $"{fileName}_{bundleId}";
        return Path.Combine(extractionRoot, uniqueDir);
    }

    /// <summary>
    /// Extracts all files that need extraction.
    /// </summary>
    public void ExtractAll()
    {
        if (!_manifest.FilesNeedExtraction)
        {
            return;
        }

        Directory.CreateDirectory(_extractionDir);

        foreach (var entry in _manifest.Files)
        {
            if (entry.NeedsExtraction || _manifest.Header.IsNetCoreApp3CompatMode)
            {
                ExtractFile(entry);
            }
        }
    }

    /// <summary>
    /// Extracts a specific file if needed.
    /// </summary>
    public string? ExtractFile(BundleFileEntry entry)
    {
        if (_extractedFiles.Contains(entry.RelativePath))
        {
            return Path.Combine(_extractionDir, entry.RelativePath);
        }

        string targetPath = Path.Combine(_extractionDir, entry.RelativePath);
        string? targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // Check if already extracted
        if (File.Exists(targetPath))
        {
            var fileInfo = new FileInfo(targetPath);
            long expectedSize = entry.IsCompressed ? entry.Size : entry.Size;
            if (fileInfo.Length == expectedSize)
            {
                _extractedFiles.Add(entry.RelativePath);
                return targetPath;
            }
        }

        // Extract the file
        using var sourceStream = _reader.CreateStream(entry.Offset, entry.IsCompressed ? entry.CompressedSize : entry.Size);
        using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (entry.IsCompressed)
        {
            using var deflateStream = new DeflateStream(sourceStream, CompressionMode.Decompress);
            deflateStream.CopyTo(targetStream);
        }
        else
        {
            sourceStream.CopyTo(targetStream);
        }

        _extractedFiles.Add(entry.RelativePath);
        return targetPath;
    }

    /// <summary>
    /// Reads file content directly from the bundle without extraction.
    /// </summary>
    public byte[] ReadFileContent(BundleFileEntry entry)
    {
        using var sourceStream = _reader.CreateStream(entry.Offset, entry.IsCompressed ? entry.CompressedSize : entry.Size);

        if (entry.IsCompressed)
        {
            using var deflateStream = new DeflateStream(sourceStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            deflateStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
        else
        {
            byte[] buffer = new byte[entry.Size];
            int totalRead = 0;
            while (totalRead < entry.Size)
            {
                int read = sourceStream.Read(buffer, totalRead, (int)(entry.Size - totalRead));
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return buffer;
        }
    }

    /// <summary>
    /// Reads file content as a string.
    /// </summary>
    public string ReadFileAsString(BundleFileEntry entry)
    {
        byte[] content = ReadFileContent(entry);
        return Encoding.UTF8.GetString(content);
    }

    /// <summary>
    /// Gets the deps.json content from the bundle.
    /// </summary>
    public string? GetDepsJsonContent()
    {
        if (_manifest.Header.HasDepsJson)
        {
            byte[] content = _reader.ReadBytesAt(_manifest.Header.DepsJsonOffset, (int)_manifest.Header.DepsJsonSize);
            return Encoding.UTF8.GetString(content);
        }

        // Fall back to finding in manifest
        var entry = _manifest.FindFile("*.deps.json") ??
                    FindFileByType(BundleFileType.DepsJson);

        return entry is not null ? ReadFileAsString(entry) : null;
    }

    /// <summary>
    /// Gets the runtimeconfig.json content from the bundle.
    /// </summary>
    public string? GetRuntimeConfigContent()
    {
        if (_manifest.Header.HasRuntimeConfig)
        {
            byte[] content = _reader.ReadBytesAt(_manifest.Header.RuntimeConfigOffset, (int)_manifest.Header.RuntimeConfigSize);
            return Encoding.UTF8.GetString(content);
        }

        // Fall back to finding in manifest
        var entry = _manifest.FindFile("*.runtimeconfig.json") ??
                    FindFileByType(BundleFileType.RuntimeConfigJson);

        return entry is not null ? ReadFileAsString(entry) : null;
    }

    private BundleFileEntry? FindFileByType(BundleFileType type)
    {
        foreach (var entry in _manifest.Files)
        {
            if (entry.Type == type)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the path to a file, extracting if necessary.
    /// </summary>
    public string? GetFilePath(string relativePath)
    {
        var entry = _manifest.FindFile(relativePath);
        if (entry is null)
        {
            return null;
        }

        if (entry.NeedsExtraction || _manifest.Header.IsNetCoreApp3CompatMode)
        {
            return ExtractFile(entry);
        }

        // For assemblies and other files that can be loaded from memory,
        // we don't need to extract - but we need to provide a path for probing
        return Path.Combine(_extractionDir, relativePath);
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
