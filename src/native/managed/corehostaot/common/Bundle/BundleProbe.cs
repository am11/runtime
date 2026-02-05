// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Provides bundle probing functionality for the runtime.
/// </summary>
public sealed class BundleProbe : IDisposable
{
    private readonly BundleExtractor _extractor;
    private static BundleProbe? s_current;

    /// <summary>
    /// The current bundle probe instance (set when running as a single-file app).
    /// </summary>
    public static BundleProbe? Current => s_current;

    /// <summary>
    /// Whether the application is running from a bundle.
    /// </summary>
    public static bool IsBundle => s_current is not null;

    private BundleProbe(BundleExtractor extractor)
    {
        _extractor = extractor;
    }

    /// <summary>
    /// Initializes bundle support for the given application.
    /// </summary>
    public static bool Initialize(string appPath, long headerOffset)
    {
        if (headerOffset == 0)
        {
            // Try to detect bundle
            headerOffset = BundleExtractor.GetBundleHeaderOffset(appPath);
        }

        if (headerOffset == 0)
        {
            return false;
        }

        var extractor = BundleExtractor.Open(appPath, headerOffset);
        if (extractor is null)
        {
            return false;
        }

        // Extract files that need extraction (native binaries)
        extractor.ExtractAll();

        s_current = new BundleProbe(extractor);
        return true;
    }

    /// <summary>
    /// The bundle manifest.
    /// </summary>
    public BundleManifest Manifest => _extractor.Manifest;

    /// <summary>
    /// The extraction directory for files that needed extraction.
    /// </summary>
    public string ExtractionDirectory => _extractor.ExtractionDirectory;

    /// <summary>
    /// Probes for a file in the bundle.
    /// </summary>
    /// <param name="path">The relative path to probe.</param>
    /// <param name="offset">Output: offset of the file in the bundle.</param>
    /// <param name="size">Output: size of the file.</param>
    /// <returns>True if the file was found in the bundle.</returns>
    public bool Probe(string path, out long offset, out long size)
    {
        offset = 0;
        size = 0;

        var entry = _extractor.Manifest.FindFile(path);
        if (entry is null)
        {
            return false;
        }

        offset = entry.Offset;
        size = entry.Size;
        return true;
    }

    /// <summary>
    /// Gets the content of a file from the bundle.
    /// </summary>
    public byte[]? GetFileContent(string path)
    {
        var entry = _extractor.Manifest.FindFile(path);
        if (entry is null)
        {
            return null;
        }

        return _extractor.ReadFileContent(entry);
    }

    /// <summary>
    /// Gets the deps.json content.
    /// </summary>
    public string? GetDepsJson()
    {
        return _extractor.GetDepsJsonContent();
    }

    /// <summary>
    /// Gets the runtimeconfig.json content.
    /// </summary>
    public string? GetRuntimeConfig()
    {
        return _extractor.GetRuntimeConfigContent();
    }

    /// <summary>
    /// Gets the path to an extracted file, or null if not extracted.
    /// </summary>
    public string? GetExtractedPath(string relativePath)
    {
        return _extractor.GetFilePath(relativePath);
    }

    /// <summary>
    /// Parses the runtime config from the bundle.
    /// </summary>
    public RuntimeConfig? ParseRuntimeConfig()
    {
        string? content = GetRuntimeConfig();
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        return RuntimeConfig.ParseFromString(content);
    }

    /// <summary>
    /// Parses the deps.json from the bundle.
    /// </summary>
    public DepsJson? ParseDepsJson()
    {
        string? content = GetDepsJson();
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        return DepsJson.ParseFromString(content);
    }

    public void Dispose()
    {
        _extractor.Dispose();
        if (s_current == this)
        {
            s_current = null;
        }
    }
}
