// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Asset types in deps.json.
/// </summary>
public enum DepsAssetType
{
    Runtime,
    Resources,
    Native,
}

/// <summary>
/// Represents an asset from deps.json.
/// </summary>
public sealed class DepsAsset
{
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string? AssemblyVersion { get; init; }
    public string? FileVersion { get; init; }
}

/// <summary>
/// Represents a library entry from deps.json.
/// </summary>
public sealed class DepsLibrary
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Path { get; init; }
    public bool Serviceable { get; init; }
    public List<string> Dependencies { get; } = [];
    public List<DepsAsset> RuntimeAssets { get; } = [];
    public List<DepsAsset> NativeAssets { get; } = [];
    public List<DepsAsset> ResourceAssets { get; } = [];
}

/// <summary>
/// Parses and represents a .deps.json file.
/// </summary>
public sealed class DepsJson
{
    public string Path { get; }
    public bool IsValid { get; private set; }
    public string RuntimeTarget { get; private set; } = "";
    public string RuntimeTargetSignature { get; private set; } = "";
    public IReadOnlyDictionary<string, DepsLibrary> Libraries => _libraries;
    public IReadOnlyDictionary<string, List<string>> RidFallbackGraph => _ridFallbackGraph;

    private readonly Dictionary<string, DepsLibrary> _libraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _ridFallbackGraph = new(StringComparer.OrdinalIgnoreCase);

    private DepsJson(string path)
    {
        Path = path;
    }

    public static DepsJson? Parse(string path)
    {
        var deps = new DepsJson(path);
        if (deps.Load())
        {
            return deps;
        }

        return null;
    }

    public static DepsJson? ParseFromString(string json, string path = "<memory>")
    {
        var deps = new DepsJson(path);
        if (deps.LoadFromString(json))
        {
            return deps;
        }

        return null;
    }

    private bool Load()
    {
        if (!File.Exists(Path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(Path);
            return LoadFromString(json);
        }
        catch
        {
            return false;
        }
    }

    private bool LoadFromString(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var root = document.RootElement;

            // Parse runtimeTarget
            if (root.TryGetProperty("runtimeTarget", out var runtimeTarget))
            {
                if (runtimeTarget.TryGetProperty("name", out var name))
                {
                    RuntimeTarget = name.GetString() ?? "";
                }

                if (runtimeTarget.TryGetProperty("signature", out var signature))
                {
                    RuntimeTargetSignature = signature.GetString() ?? "";
                }
            }

            // Parse libraries
            if (root.TryGetProperty("libraries", out var libraries))
            {
                ParseLibraries(libraries);
            }

            // Parse targets
            if (root.TryGetProperty("targets", out var targets))
            {
                // Find the RID-specific target
                string ridTarget = RuntimeTarget;
                if (targets.TryGetProperty(ridTarget, out var target))
                {
                    ParseTarget(target);
                }
            }

            // Parse runtimes (RID fallback graph)
            if (root.TryGetProperty("runtimes", out var runtimes))
            {
                ParseRidFallbackGraph(runtimes);
            }

            IsValid = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ParseLibraries(JsonElement libraries)
    {
        foreach (var lib in libraries.EnumerateObject())
        {
            string fullName = lib.Name;
            int slashIndex = fullName.IndexOf('/');
            string name = slashIndex >= 0 ? fullName[..slashIndex] : fullName;
            string version = slashIndex >= 0 ? fullName[(slashIndex + 1)..] : "";

            var library = new DepsLibrary
            {
                Name = name,
                Version = version,
                Type = lib.Value.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                Path = lib.Value.TryGetProperty("path", out var path) ? path.GetString() : null,
                Serviceable = lib.Value.TryGetProperty("serviceable", out var serviceable) && serviceable.GetBoolean(),
            };

            _libraries[fullName] = library;
        }
    }

    private void ParseTarget(JsonElement target)
    {
        foreach (var lib in target.EnumerateObject())
        {
            string fullName = lib.Name;
            if (!_libraries.TryGetValue(fullName, out var library))
            {
                continue;
            }

            // Parse dependencies
            if (lib.Value.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dep in dependencies.EnumerateObject())
                {
                    library.Dependencies.Add($"{dep.Name}/{dep.Value.GetString()}");
                }
            }

            // Parse runtime assets
            if (lib.Value.TryGetProperty("runtime", out var runtime))
            {
                ParseAssets(runtime, library.RuntimeAssets);
            }

            // Parse native assets
            if (lib.Value.TryGetProperty("native", out var native))
            {
                ParseAssets(native, library.NativeAssets);
            }

            // Parse resource assets
            if (lib.Value.TryGetProperty("resources", out var resources))
            {
                ParseAssets(resources, library.ResourceAssets);
            }
        }
    }

    private static void ParseAssets(JsonElement assetsElement, List<DepsAsset> assets)
    {
        foreach (var asset in assetsElement.EnumerateObject())
        {
            string relativePath = asset.Name.Replace('\\', '/');
            string name = System.IO.Path.GetFileNameWithoutExtension(relativePath);

            var depsAsset = new DepsAsset
            {
                Name = name,
                RelativePath = relativePath,
                AssemblyVersion = asset.Value.TryGetProperty("assemblyVersion", out var av) ? av.GetString() : null,
                FileVersion = asset.Value.TryGetProperty("fileVersion", out var fv) ? fv.GetString() : null,
            };

            assets.Add(depsAsset);
        }
    }

    private void ParseRidFallbackGraph(JsonElement runtimes)
    {
        foreach (var rid in runtimes.EnumerateObject())
        {
            var fallbacks = new List<string>();
            if (rid.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var fallback in rid.Value.EnumerateArray())
                {
                    string? fb = fallback.GetString();
                    if (!string.IsNullOrEmpty(fb))
                    {
                        fallbacks.Add(fb);
                    }
                }
            }

            _ridFallbackGraph[rid.Name] = fallbacks;
        }
    }

    /// <summary>
    /// Gets all assembly paths that should be added to TPA.
    /// </summary>
    public IEnumerable<string> GetAssemblyPaths(string basePath)
    {
        foreach (var lib in _libraries.Values)
        {
            foreach (var asset in lib.RuntimeAssets)
            {
                string assetPath = string.IsNullOrEmpty(lib.Path)
                    ? System.IO.Path.Combine(basePath, asset.RelativePath)
                    : System.IO.Path.Combine(basePath, lib.Path, asset.RelativePath);

                yield return assetPath;
            }
        }
    }

    /// <summary>
    /// Gets all native library paths.
    /// </summary>
    public IEnumerable<string> GetNativeLibraryPaths(string basePath)
    {
        foreach (var lib in _libraries.Values)
        {
            foreach (var asset in lib.NativeAssets)
            {
                string assetPath = string.IsNullOrEmpty(lib.Path)
                    ? System.IO.Path.Combine(basePath, asset.RelativePath)
                    : System.IO.Path.Combine(basePath, lib.Path, asset.RelativePath);

                yield return assetPath;
            }
        }
    }
}
