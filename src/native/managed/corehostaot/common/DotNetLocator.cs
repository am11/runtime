// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Utilities for locating .NET installations and host components.
/// </summary>
internal static class DotNetLocator
{
    private const string HostFxrName = "hostfxr";

    /// <summary>
    /// Gets the path to the hostfxr library.
    /// </summary>
    /// <param name="assemblyPath">Optional path to the component's assembly.</param>
    /// <param name="dotnetRoot">Optional path to the dotnet root directory.</param>
    /// <returns>The path to hostfxr, or null if not found.</returns>
    public static string? GetHostFxrPath(string? assemblyPath = null, string? dotnetRoot = null)
    {
        // If dotnet_root is specified, use it directly
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            return GetHostFxrFromDotNetRoot(dotnetRoot);
        }

        // If assembly_path is specified, look relative to it
        if (!string.IsNullOrEmpty(assemblyPath))
        {
            string? result = GetHostFxrFromAssemblyPath(assemblyPath);
            if (result is not null)
            {
                return result;
            }
        }

        // Try to find from environment or default locations
        return GetHostFxrFromEnvironmentOrDefault();
    }

    private static string? GetHostFxrFromDotNetRoot(string dotnetRoot)
    {
        string hostFxrBase = Path.Combine(dotnetRoot, "host", "fxr");
        return GetLatestHostFxr(hostFxrBase);
    }

    private static string? GetHostFxrFromAssemblyPath(string assemblyPath)
    {
        // Look for a .NET installation relative to the assembly
        string? directory = Path.GetDirectoryName(assemblyPath);
        if (directory is null)
        {
            return null;
        }

        // Try to find the dotnet root by walking up the directory tree
        while (!string.IsNullOrEmpty(directory))
        {
            string hostFxrBase = Path.Combine(directory, "host", "fxr");
            if (Directory.Exists(hostFxrBase))
            {
                string? result = GetLatestHostFxr(hostFxrBase);
                if (result is not null)
                {
                    return result;
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static string? GetHostFxrFromEnvironmentOrDefault()
    {
        // Check DOTNET_ROOT environment variable
        string? dotnetRoot = Environment.GetEnvironmentVariable(
            RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "DOTNET_ROOT"
                : $"DOTNET_ROOT_{RuntimeInformation.ProcessArchitecture}");

        if (string.IsNullOrEmpty(dotnetRoot))
        {
            dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        }

        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            string? result = GetHostFxrFromDotNetRoot(dotnetRoot);
            if (result is not null)
            {
                return result;
            }
        }

        // Try default installation locations
        string[] defaultRoots = GetDefaultDotNetRoots();
        foreach (string root in defaultRoots)
        {
            if (Directory.Exists(root))
            {
                string? result = GetHostFxrFromDotNetRoot(root);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the default .NET root directory from environment or default locations.
    /// </summary>
    public static string? GetDefaultDotNetRoot()
    {
        // Check DOTNET_ROOT environment variable
        string? dotnetRoot = Environment.GetEnvironmentVariable(
            RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "DOTNET_ROOT"
                : $"DOTNET_ROOT_{RuntimeInformation.ProcessArchitecture}");

        if (string.IsNullOrEmpty(dotnetRoot))
        {
            dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        }

        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
        {
            return dotnetRoot;
        }

        // Try default installation locations
        string[] defaultRoots = GetDefaultDotNetRoots();
        foreach (string root in defaultRoots)
        {
            if (Directory.Exists(root))
            {
                return root;
            }
        }

        return null;
    }

    private static string[] GetDefaultDotNetRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return [Path.Combine(programFiles, "dotnet")];
        }
        else if (OperatingSystem.IsMacOS())
        {
            return ["/usr/local/share/dotnet", "/opt/homebrew/share/dotnet"];
        }
        else
        {
            // Linux and others
            return ["/usr/share/dotnet", "/usr/lib/dotnet"];
        }
    }

    private static string? GetLatestHostFxr(string hostFxrBase)
    {
        if (!Directory.Exists(hostFxrBase))
        {
            return null;
        }

        // Find all version directories and pick the highest version
        Version? highestVersion = null;
        string? highestVersionDir = null;

        foreach (string dir in Directory.GetDirectories(hostFxrBase))
        {
            string versionString = Path.GetFileName(dir);
            if (Version.TryParse(versionString, out Version? version))
            {
                if (highestVersion is null || version > highestVersion)
                {
                    highestVersion = version;
                    highestVersionDir = dir;
                }
            }
        }

        if (highestVersionDir is null)
        {
            return null;
        }

        string libraryName = GetHostFxrLibraryName();
        string libraryPath = Path.Combine(highestVersionDir, libraryName);

        return File.Exists(libraryPath) ? libraryPath : null;
    }

    private static string GetHostFxrLibraryName()
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{HostFxrName}.dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            return $"lib{HostFxrName}.dylib";
        }
        else
        {
            return $"lib{HostFxrName}.so";
        }
    }
}
