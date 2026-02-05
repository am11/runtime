// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Provides multi-level lookup for .NET installations.
/// </summary>
public static class MultiLevelLookup
{
    /// <summary>
    /// Checks if multi-level lookup is enabled.
    /// Can be disabled by setting DOTNET_MULTILEVEL_LOOKUP=0
    /// </summary>
    public static bool IsEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP");
        if (!string.IsNullOrEmpty(value))
        {
            return value == "1";
        }

        // Enabled by default
        return true;
    }

    /// <summary>
    /// Gets all framework lookup locations in priority order.
    /// </summary>
    public static List<string> GetFrameworkLocations(string? dotnetDir, bool disableMultilevelLookup = false)
    {
        var locations = new List<string>();
        bool multilevelEnabled = !disableMultilevelLookup && IsEnabled();

        // First priority: provided dotnet directory
        if (!string.IsNullOrEmpty(dotnetDir))
        {
            string normalizedDir = dotnetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(normalizedDir))
            {
                locations.Add(normalizedDir);
            }
        }

        if (!multilevelEnabled)
        {
            return locations;
        }

        // Second priority: global .NET directories
        foreach (string globalDir in GetGlobalDotnetDirs())
        {
            // Avoid duplicate paths
            bool isDuplicate = false;
            foreach (string existing in locations)
            {
                if (ArePathsEqual(existing, globalDir))
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                locations.Add(globalDir);
            }
        }

        return locations;
    }

    /// <summary>
    /// Gets the global .NET installation directories.
    /// </summary>
    public static List<string> GetGlobalDotnetDirs()
    {
        var dirs = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            // On Windows: check registry, then default location
            string? registeredDir = GetWindowsRegisteredInstallLocation();
            if (!string.IsNullOrEmpty(registeredDir) && Directory.Exists(registeredDir))
            {
                dirs.Add(registeredDir);
            }

            string? defaultDir = GetWindowsDefaultInstallLocation();
            if (!string.IsNullOrEmpty(defaultDir) && Directory.Exists(defaultDir))
            {
                // Avoid duplicate
                if (registeredDir is null || !ArePathsEqual(registeredDir, defaultDir))
                {
                    dirs.Add(defaultDir);
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            // On macOS: check self-registered config, then default locations
            string? selfRegistered = GetUnixSelfRegisteredLocation();
            if (!string.IsNullOrEmpty(selfRegistered) && Directory.Exists(selfRegistered))
            {
                dirs.Add(selfRegistered);
            }

            // Default locations
            string[] defaultLocations = ["/usr/local/share/dotnet", "/opt/homebrew/share/dotnet"];
            foreach (string loc in defaultLocations)
            {
                if (Directory.Exists(loc) && !ContainsPath(dirs, loc))
                {
                    dirs.Add(loc);
                }
            }
        }
        else
        {
            // On Linux: check self-registered config, then default locations
            string? selfRegistered = GetUnixSelfRegisteredLocation();
            if (!string.IsNullOrEmpty(selfRegistered) && Directory.Exists(selfRegistered))
            {
                dirs.Add(selfRegistered);
            }

            // Default locations
            string[] defaultLocations = ["/usr/share/dotnet", "/usr/lib/dotnet"];
            foreach (string loc in defaultLocations)
            {
                if (Directory.Exists(loc) && !ContainsPath(dirs, loc))
                {
                    dirs.Add(loc);
                }
            }
        }

        return dirs;
    }

    private static string? GetWindowsRegisteredInstallLocation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            // Try HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation
            string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            string keyPath = $@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}";

            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowsDefaultInstallLocation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Path.Combine(programFiles, "dotnet");
    }

    private static string? GetUnixSelfRegisteredLocation()
    {
        // Check /etc/dotnet/install_location_<arch>
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string configPath = $"/etc/dotnet/install_location_{arch}";

        // Allow test override
        string? testOverride = Environment.GetEnvironmentVariable("_DOTNET_TEST_INSTALL_LOCATION_PATH");
        if (!string.IsNullOrEmpty(testOverride))
        {
            configPath = Path.Combine(testOverride, $"install_location_{arch}");
        }

        try
        {
            if (File.Exists(configPath))
            {
                string? location = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(location))
                {
                    return location;
                }
            }
        }
        catch
        {
            // Ignore errors reading config
        }

        return null;
    }

    private static bool ContainsPath(List<string> paths, string path)
    {
        foreach (string existing in paths)
        {
            if (ArePathsEqual(existing, path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ArePathsEqual(string path1, string path2)
    {
        // Normalize paths
        path1 = Path.GetFullPath(path1).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        path2 = Path.GetFullPath(path2).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Case-insensitive on Windows, case-sensitive elsewhere
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(path1, path2, comparison);
    }
}
