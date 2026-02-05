// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Roll forward options for framework resolution.
/// </summary>
public enum RollForwardOption
{
    /// <summary>
    /// Do not roll forward. Only bind to specified version.
    /// </summary>
    Disable,

    /// <summary>
    /// Roll forward to the highest patch version.
    /// </summary>
    LatestPatch,

    /// <summary>
    /// Roll forward on missing minor version. This is the default.
    /// </summary>
    Minor,

    /// <summary>
    /// Roll forward to highest minor version.
    /// </summary>
    LatestMinor,

    /// <summary>
    /// Roll forward on missing major version.
    /// </summary>
    Major,

    /// <summary>
    /// Roll forward to highest major version.
    /// </summary>
    LatestMajor,
}

public static class RollForwardOptionExtensions
{
    public static RollForwardOption Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return RollForwardOption.Minor;
        }

        return value.ToLowerInvariant() switch
        {
            "disable" => RollForwardOption.Disable,
            "latestpatch" => RollForwardOption.LatestPatch,
            "minor" => RollForwardOption.Minor,
            "latestminor" => RollForwardOption.LatestMinor,
            "major" => RollForwardOption.Major,
            "latestmajor" => RollForwardOption.LatestMajor,
            _ => RollForwardOption.Minor,
        };
    }
}
