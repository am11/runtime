// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Resolves frameworks from the installed shared frameworks.
/// </summary>
public sealed class FrameworkResolver
{
    private readonly string _dotnetRoot;
    private readonly bool _disableMultilevelLookup;

    public FrameworkResolver(string dotnetRoot, bool disableMultilevelLookup = false)
    {
        _dotnetRoot = dotnetRoot;
        _disableMultilevelLookup = disableMultilevelLookup;
    }

    /// <summary>
    /// Resolves all frameworks from a runtime config.
    /// </summary>
    public bool ResolveFrameworks(RuntimeConfig config, List<FrameworkReference> resolvedFrameworks)
    {
        // Get all lookup locations
        var locations = MultiLevelLookup.GetFrameworkLocations(_dotnetRoot, _disableMultilevelLookup);

        foreach (var fxRef in config.Frameworks)
        {
            if (!ResolveFramework(fxRef, locations))
            {
                return false;
            }

            resolvedFrameworks.Add(fxRef);
        }

        return true;
    }

    /// <summary>
    /// Resolves a single framework reference.
    /// </summary>
    public bool ResolveFramework(FrameworkReference fxRef)
    {
        var locations = MultiLevelLookup.GetFrameworkLocations(_dotnetRoot, _disableMultilevelLookup);
        return ResolveFramework(fxRef, locations);
    }

    private static bool ResolveFramework(FrameworkReference fxRef, List<string> lookupLocations)
    {
        // Get all available versions across all lookup locations
        List<(string Path, FrameworkVersion Version)> availableVersions = [];

        foreach (string dotnetRoot in lookupLocations)
        {
            string frameworkDir = Path.Combine(dotnetRoot, "shared", fxRef.Name);
            if (!Directory.Exists(frameworkDir))
            {
                continue;
            }

            foreach (string versionDir in Directory.GetDirectories(frameworkDir))
            {
                string versionName = Path.GetFileName(versionDir);
                if (FrameworkVersion.TryParse(versionName, out var version))
                {
                    availableVersions.Add((versionDir, version));
                }
            }
        }

        if (availableVersions.Count == 0)
        {
            return false;
        }

        // Sort by version (ascending)
        availableVersions.Sort((a, b) => a.Version.CompareTo(b.Version));

        // Find the best match based on roll forward policy
        var requestedVersion = fxRef.VersionNumber;
        (string Path, FrameworkVersion Version)? bestMatch = FindBestMatch(availableVersions, requestedVersion, fxRef.RollForward);

        if (bestMatch is null)
        {
            return false;
        }

        fxRef.ResolvedPath = bestMatch.Value.Path;
        fxRef.ResolvedVersion = bestMatch.Value.Version.ToString();

        return true;
    }

    private static (string Path, FrameworkVersion Version)? FindBestMatch(
        List<(string Path, FrameworkVersion Version)> availableVersions,
        FrameworkVersion requested,
        RollForwardOption rollForward)
    {
        (string Path, FrameworkVersion Version)? exactMatch = null;
        (string Path, FrameworkVersion Version)? patchMatch = null;
        (string Path, FrameworkVersion Version)? minorMatch = null;
        (string Path, FrameworkVersion Version)? majorMatch = null;

        foreach (var available in availableVersions)
        {
            var version = available.Version;

            // Check for exact match
            if (version.Major == requested.Major &&
                version.Minor == requested.Minor &&
                version.Patch == requested.Patch)
            {
                exactMatch = available;
            }
            // Check for patch-level match (same major.minor, higher or equal patch)
            else if (version.Major == requested.Major &&
                     version.Minor == requested.Minor &&
                     version.Patch >= requested.Patch)
            {
                if (patchMatch is null || version > patchMatch.Value.Version)
                {
                    patchMatch = available;
                }
            }
            // Check for minor-level match (same major, higher or equal minor)
            else if (version.Major == requested.Major &&
                     (version.Minor > requested.Minor ||
                      (version.Minor == requested.Minor && version.Patch >= requested.Patch)))
            {
                if (rollForward == RollForwardOption.LatestMinor)
                {
                    // Pick highest minor
                    if (minorMatch is null || version > minorMatch.Value.Version)
                    {
                        minorMatch = available;
                    }
                }
                else
                {
                    // Pick lowest compatible minor
                    if (minorMatch is null || version < minorMatch.Value.Version)
                    {
                        minorMatch = available;
                    }
                }
            }
            // Check for major-level match (higher or equal major)
            else if (version.Major > requested.Major ||
                     (version.Major == requested.Major && version.Minor >= requested.Minor))
            {
                if (rollForward == RollForwardOption.LatestMajor)
                {
                    // Pick highest major
                    if (majorMatch is null || version > majorMatch.Value.Version)
                    {
                        majorMatch = available;
                    }
                }
                else
                {
                    // Pick lowest compatible major
                    if (majorMatch is null || version < majorMatch.Value.Version)
                    {
                        majorMatch = available;
                    }
                }
            }
        }

        return rollForward switch
        {
            RollForwardOption.Disable => exactMatch,
            RollForwardOption.LatestPatch => patchMatch ?? exactMatch,
            RollForwardOption.Minor => minorMatch ?? patchMatch ?? exactMatch,
            RollForwardOption.LatestMinor => minorMatch ?? patchMatch ?? exactMatch,
            RollForwardOption.Major => majorMatch ?? minorMatch ?? patchMatch ?? exactMatch,
            RollForwardOption.LatestMajor => majorMatch ?? minorMatch ?? patchMatch ?? exactMatch,
            _ => minorMatch ?? patchMatch ?? exactMatch,
        };
    }

    /// <summary>
    /// Gets all installed frameworks at the given dotnet root (including multi-level lookup).
    /// </summary>
    public List<(string Name, string Version, string Path)> GetInstalledFrameworks()
    {
        var result = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locations = MultiLevelLookup.GetFrameworkLocations(_dotnetRoot, _disableMultilevelLookup);

        foreach (string dotnetRoot in locations)
        {
            string sharedDir = Path.Combine(dotnetRoot, "shared");
            if (!Directory.Exists(sharedDir))
            {
                continue;
            }

            foreach (string frameworkDir in Directory.GetDirectories(sharedDir))
            {
                string frameworkName = Path.GetFileName(frameworkDir);
                foreach (string versionDir in Directory.GetDirectories(frameworkDir))
                {
                    string version = Path.GetFileName(versionDir);
                    string key = $"{frameworkName}/{version}";

                    // Avoid duplicates (first location wins)
                    if (seen.Add(key))
                    {
                        result.Add((frameworkName, version, versionDir));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all installed SDKs at the given dotnet root (including multi-level lookup).
    /// </summary>
    public List<(string Version, string Path)> GetInstalledSdks()
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locations = MultiLevelLookup.GetFrameworkLocations(_dotnetRoot, _disableMultilevelLookup);

        foreach (string dotnetRoot in locations)
        {
            string sdkDir = Path.Combine(dotnetRoot, "sdk");
            if (!Directory.Exists(sdkDir))
            {
                continue;
            }

            foreach (string versionDir in Directory.GetDirectories(sdkDir))
            {
                string version = Path.GetFileName(versionDir);
                // Skip workload directories and other non-version directories
                if (FrameworkVersion.TryParse(version, out _))
                {
                    // Avoid duplicates (first location wins)
                    if (seen.Add(version))
                    {
                        result.Add((version, versionDir));
                    }
                }
            }
        }

        return result;
    }
}
