// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Manages runtime properties for CoreCLR initialization.
/// </summary>
internal sealed class RuntimePropertyBag
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);

    public void Add(string key, string value)
    {
        _properties[key] = value;
    }

    public bool TryGet(string key, out string? value)
    {
        return _properties.TryGetValue(key, out value);
    }

    public void Remove(string key)
    {
        _properties.Remove(key);
    }

    public int Count => _properties.Count;

    public (string[] Keys, string[] Values) ToArrays()
    {
        var keys = new string[_properties.Count];
        var values = new string[_properties.Count];
        int i = 0;
        foreach (var kvp in _properties)
        {
            keys[i] = kvp.Key;
            values[i] = kvp.Value;
            i++;
        }

        return (keys, values);
    }

    /// <summary>
    /// Builds the TRUSTED_PLATFORM_ASSEMBLIES property from framework paths.
    /// </summary>
    public static string BuildTpaList(IEnumerable<string> frameworkPaths)
    {
        var sb = new StringBuilder();
        char separator = OperatingSystem.IsWindows() ? ';' : ':';

        foreach (string frameworkPath in frameworkPaths)
        {
            if (!Directory.Exists(frameworkPath))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(frameworkPath, "*.dll"))
            {
                if (sb.Length > 0)
                {
                    sb.Append(separator);
                }

                sb.Append(file);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the NATIVE_DLL_SEARCH_DIRECTORIES property.
    /// </summary>
    public static string BuildNativeSearchDirectories(IEnumerable<string> directories)
    {
        char separator = OperatingSystem.IsWindows() ? ';' : ':';
        return string.Join(separator, directories);
    }
}
