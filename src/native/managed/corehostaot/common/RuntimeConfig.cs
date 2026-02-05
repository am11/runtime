// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Parses and represents a .runtimeconfig.json file.
/// </summary>
public sealed class RuntimeConfig
{
    public string Path { get; }
    public bool IsValid { get; private set; }
    public string? Tfm { get; private set; }
    public bool IsFrameworkDependent { get; private set; }
    public RollForwardOption RollForward { get; private set; } = RollForwardOption.Minor;
    public bool ApplyPatches { get; private set; } = true;
    public IReadOnlyList<FrameworkReference> Frameworks => _frameworks;
    public IReadOnlyDictionary<string, string> Properties => _properties;
    public IReadOnlyList<string> ProbePaths => _probePaths;

    private readonly List<FrameworkReference> _frameworks = [];
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly List<string> _probePaths = [];

    private RuntimeConfig(string path)
    {
        Path = path;
    }

    public static RuntimeConfig? Parse(string path)
    {
        var config = new RuntimeConfig(path);
        if (config.Load())
        {
            return config;
        }

        return null;
    }

    public static RuntimeConfig? ParseFromString(string json, string path = "<memory>")
    {
        var config = new RuntimeConfig(path);
        if (config.LoadFromString(json))
        {
            return config;
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
            if (!root.TryGetProperty("runtimeOptions", out var runtimeOptions))
            {
                return false;
            }

            ParseRuntimeOptions(runtimeOptions);
            IsValid = true;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ParseRuntimeOptions(JsonElement runtimeOptions)
    {
        // Parse tfm
        if (runtimeOptions.TryGetProperty("tfm", out var tfm))
        {
            Tfm = tfm.GetString();
        }

        // Parse rollForward
        if (runtimeOptions.TryGetProperty("rollForward", out var rollForward))
        {
            RollForward = RollForwardOptionExtensions.Parse(rollForward.GetString());
        }

        // Parse applyPatches (deprecated but still supported)
        if (runtimeOptions.TryGetProperty("applyPatches", out var applyPatches))
        {
            ApplyPatches = applyPatches.GetBoolean();
        }

        // Parse single framework reference
        if (runtimeOptions.TryGetProperty("framework", out var framework))
        {
            var fxRef = ParseFramework(framework);
            if (fxRef is not null)
            {
                _frameworks.Add(fxRef);
                IsFrameworkDependent = true;
            }
        }

        // Parse multiple framework references
        if (runtimeOptions.TryGetProperty("frameworks", out var frameworks) && frameworks.ValueKind == JsonValueKind.Array)
        {
            foreach (var fx in frameworks.EnumerateArray())
            {
                var fxRef = ParseFramework(fx);
                if (fxRef is not null)
                {
                    _frameworks.Add(fxRef);
                    IsFrameworkDependent = true;
                }
            }
        }

        // Parse included frameworks (for composites)
        if (runtimeOptions.TryGetProperty("includedFrameworks", out var includedFrameworks) && includedFrameworks.ValueKind == JsonValueKind.Array)
        {
            foreach (var fx in includedFrameworks.EnumerateArray())
            {
                var fxRef = ParseFramework(fx);
                if (fxRef is not null)
                {
                    _frameworks.Add(fxRef);
                }
            }
        }

        // Parse configProperties
        if (runtimeOptions.TryGetProperty("configProperties", out var configProperties) && configProperties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in configProperties.EnumerateObject())
            {
                string? value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null
                };

                if (value is not null)
                {
                    _properties[property.Name] = value;
                }
            }
        }

        // Parse additionalProbingPaths
        if (runtimeOptions.TryGetProperty("additionalProbingPaths", out var probingPaths) && probingPaths.ValueKind == JsonValueKind.Array)
        {
            foreach (var probePath in probingPaths.EnumerateArray())
            {
                string? path = probePath.GetString();
                if (!string.IsNullOrEmpty(path))
                {
                    _probePaths.Add(path);
                }
            }
        }
    }

    private FrameworkReference? ParseFramework(JsonElement framework)
    {
        if (!framework.TryGetProperty("name", out var nameElement))
        {
            return null;
        }

        string? name = nameElement.GetString();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string version = "";
        if (framework.TryGetProperty("version", out var versionElement))
        {
            version = versionElement.GetString() ?? "";
        }

        var fxRef = new FrameworkReference(name, version)
        {
            RollForward = RollForward,
            ApplyPatches = ApplyPatches,
        };

        // Framework-specific rollForward overrides global
        if (framework.TryGetProperty("rollForward", out var rollForward))
        {
            fxRef = new FrameworkReference(name, version)
            {
                RollForward = RollForwardOptionExtensions.Parse(rollForward.GetString()),
                ApplyPatches = fxRef.ApplyPatches,
            };
        }

        return fxRef;
    }
}
