// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Represents a framework reference from a runtime config.
/// </summary>
public sealed class FrameworkReference
{
    public string Name { get; }
    public string Version { get; }
    public FrameworkVersion VersionNumber { get; }
    public RollForwardOption RollForward { get; init; } = RollForwardOption.Minor;
    public bool ApplyPatches { get; init; } = true;

    public FrameworkReference(string name, string version)
    {
        Name = name;
        Version = version;
        _ = FrameworkVersion.TryParse(version, out var versionNumber);
        VersionNumber = versionNumber;
    }

    /// <summary>
    /// Path to the resolved framework, set after resolution.
    /// </summary>
    public string? ResolvedPath { get; set; }

    /// <summary>
    /// The resolved version, set after resolution.
    /// </summary>
    public string? ResolvedVersion { get; set; }
}
