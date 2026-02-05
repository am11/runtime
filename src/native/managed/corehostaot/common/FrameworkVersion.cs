// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Represents a parsed version with major.minor.patch[-prerelease][+build].
/// </summary>
public readonly struct FrameworkVersion : IComparable<FrameworkVersion>, IEquatable<FrameworkVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string PreRelease { get; }
    public string Build { get; }

    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    public FrameworkVersion(int major, int minor, int patch, string preRelease = "", string build = "")
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease ?? "";
        Build = build ?? "";
    }

    public static bool TryParse(string? version, out FrameworkVersion result)
    {
        result = default;
        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        // Handle build metadata (+)
        string build = "";
        int buildIndex = version.IndexOf('+');
        if (buildIndex >= 0)
        {
            build = version[(buildIndex + 1)..];
            version = version[..buildIndex];
        }

        // Handle pre-release (-)
        string preRelease = "";
        int preReleaseIndex = version.IndexOf('-');
        if (preReleaseIndex >= 0)
        {
            preRelease = version[(preReleaseIndex + 1)..];
            version = version[..preReleaseIndex];
        }

        // Parse major.minor.patch
        string[] parts = version.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int major) || major < 0)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int minor) || minor < 0)
        {
            return false;
        }

        int patch = 0;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out patch) || patch < 0))
        {
            return false;
        }

        result = new FrameworkVersion(major, minor, patch, preRelease, build);
        return true;
    }

    public int CompareTo(FrameworkVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        // Pre-release versions have lower precedence than release versions
        if (IsPreRelease && !other.IsPreRelease)
        {
            return -1;
        }

        if (!IsPreRelease && other.IsPreRelease)
        {
            return 1;
        }

        return string.Compare(PreRelease, other.PreRelease, StringComparison.Ordinal);
    }

    public bool Equals(FrameworkVersion other)
    {
        return Major == other.Major
            && Minor == other.Minor
            && Patch == other.Patch
            && PreRelease == other.PreRelease;
    }

    public override bool Equals(object? obj) => obj is FrameworkVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    public override string ToString()
    {
        string result = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(PreRelease))
        {
            result += $"-{PreRelease}";
        }

        if (!string.IsNullOrEmpty(Build))
        {
            result += $"+{Build}";
        }

        return result;
    }

    public static bool operator ==(FrameworkVersion left, FrameworkVersion right) => left.Equals(right);
    public static bool operator !=(FrameworkVersion left, FrameworkVersion right) => !left.Equals(right);
    public static bool operator <(FrameworkVersion left, FrameworkVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(FrameworkVersion left, FrameworkVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(FrameworkVersion left, FrameworkVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(FrameworkVersion left, FrameworkVersion right) => left.CompareTo(right) >= 0;
}
