// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Manages the lifecycle of the CoreCLR runtime.
/// </summary>
internal sealed class RuntimeHost : IDisposable
{
    private readonly CoreClrLoader _loader;
    private readonly RuntimePropertyBag _properties;
    private bool _isInitialized;

    private RuntimeHost(CoreClrLoader loader, RuntimePropertyBag properties)
    {
        _loader = loader;
        _properties = properties;
    }

    /// <summary>
    /// Creates a RuntimeHost from resolved frameworks.
    /// </summary>
    public static RuntimeHost? Create(
        IReadOnlyList<FrameworkReference> resolvedFrameworks,
        string appPath,
        string? depsFile,
        IDictionary<string, string>? additionalProperties)
    {
        if (resolvedFrameworks.Count == 0)
        {
            return null;
        }

        // Find coreclr in the first framework (Microsoft.NETCore.App)
        string? coreclrPath = null;
        var frameworkPaths = new List<string>();
        var nativeSearchPaths = new List<string>();

        foreach (var fx in resolvedFrameworks)
        {
            if (fx.ResolvedPath is null)
            {
                continue;
            }

            frameworkPaths.Add(fx.ResolvedPath);
            nativeSearchPaths.Add(fx.ResolvedPath);

            if (coreclrPath is null && fx.Name == "Microsoft.NETCore.App")
            {
                string coreclrName = GetCoreclrName();
                string potentialPath = Path.Combine(fx.ResolvedPath, coreclrName);
                if (File.Exists(potentialPath))
                {
                    coreclrPath = potentialPath;
                }
            }
        }

        if (coreclrPath is null)
        {
            return null;
        }

        // Load CoreCLR
        var loader = CoreClrLoader.Load(coreclrPath);
        if (loader is null)
        {
            return null;
        }

        // Build properties
        var properties = new RuntimePropertyBag();

        // Add framework paths
        string appDir = Path.GetDirectoryName(appPath) ?? ".";
        frameworkPaths.Insert(0, appDir);
        nativeSearchPaths.Insert(0, appDir);

        // Build TPA list - if deps.json exists, use it for more precise assembly list
        string tpaList;
        if (!string.IsNullOrEmpty(depsFile) && File.Exists(depsFile))
        {
            var deps = DepsJson.Parse(depsFile);
            if (deps is not null && deps.IsValid)
            {
                tpaList = BuildTpaFromDeps(deps, appDir, frameworkPaths);
            }
            else
            {
                tpaList = RuntimePropertyBag.BuildTpaList(frameworkPaths);
            }
        }
        else
        {
            tpaList = RuntimePropertyBag.BuildTpaList(frameworkPaths);
        }

        properties.Add(RuntimePropertyNames.TrustedPlatformAssemblies, tpaList);
        properties.Add(RuntimePropertyNames.NativeDllSearchDirectories, RuntimePropertyBag.BuildNativeSearchDirectories(nativeSearchPaths));
        properties.Add(RuntimePropertyNames.PlatformResourceRoots, RuntimePropertyBag.BuildNativeSearchDirectories(frameworkPaths));
        properties.Add(RuntimePropertyNames.AppContextBaseDirectory, appDir + Path.DirectorySeparatorChar);

        if (!string.IsNullOrEmpty(depsFile))
        {
            properties.Add(RuntimePropertyNames.AppContextDepsFiles, depsFile);
        }

        // Add additional properties
        if (additionalProperties is not null)
        {
            foreach (var kvp in additionalProperties)
            {
                properties.Add(kvp.Key, kvp.Value);
            }
        }

        return new RuntimeHost(loader, properties);
    }

    /// <summary>
    /// Initializes the runtime.
    /// </summary>
    public StatusCode Initialize(string appPath)
    {
        if (_isInitialized)
        {
            return StatusCode.HostInvalidState;
        }

        var (keys, values) = _properties.ToArrays();
        var result = _loader.Initialize(
            appPath,
            "clrhost",
            keys,
            values);

        if (result == StatusCode.Success)
        {
            _isInitialized = true;
        }

        return result;
    }

    /// <summary>
    /// Executes a managed assembly.
    /// </summary>
    public StatusCode ExecuteAssembly(string assemblyPath, string[] args, out int exitCode)
    {
        exitCode = -1;

        if (!_isInitialized)
        {
            var initResult = Initialize(assemblyPath);
            if (initResult != StatusCode.Success)
            {
                return initResult;
            }
        }

        return _loader.ExecuteAssembly(assemblyPath, args, out exitCode);
    }

    /// <summary>
    /// Gets a runtime delegate.
    /// </summary>
    public StatusCode GetDelegate(
        string assemblyName,
        string typeName,
        string methodName,
        out nint delegatePtr)
    {
        delegatePtr = 0;

        if (!_isInitialized)
        {
            return StatusCode.HostInvalidState;
        }

        return _loader.CreateDelegate(assemblyName, typeName, methodName, out delegatePtr);
    }

    private static string GetCoreclrName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "coreclr.dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            return "libcoreclr.dylib";
        }
        else
        {
            return "libcoreclr.so";
        }
    }

    /// <summary>
    /// Builds TPA list from deps.json entries.
    /// </summary>
    private static string BuildTpaFromDeps(DepsJson deps, string appDir, List<string> frameworkPaths)
    {
        var tpaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tpaList = new List<string>();
        char separator = OperatingSystem.IsWindows() ? ';' : ':';

        // Add assemblies from deps.json
        foreach (string assemblyPath in deps.GetAssemblyPaths(appDir))
        {
            string fileName = Path.GetFileName(assemblyPath);
            if (tpaSet.Add(fileName))
            {
                // Check if file exists in app dir first
                string appFile = Path.Combine(appDir, fileName);
                if (File.Exists(appFile))
                {
                    tpaList.Add(appFile);
                }
                else if (File.Exists(assemblyPath))
                {
                    tpaList.Add(assemblyPath);
                }
                else
                {
                    // Look in framework paths
                    foreach (string fxPath in frameworkPaths)
                    {
                        string fxFile = Path.Combine(fxPath, fileName);
                        if (File.Exists(fxFile))
                        {
                            tpaList.Add(fxFile);
                            break;
                        }
                    }
                }
            }
        }

        // Fall back to adding all dlls from framework paths for any missing assemblies
        foreach (string fxPath in frameworkPaths)
        {
            if (!Directory.Exists(fxPath))
            {
                continue;
            }

            foreach (string dllFile in Directory.GetFiles(fxPath, "*.dll"))
            {
                string fileName = Path.GetFileName(dllFile);
                if (tpaSet.Add(fileName))
                {
                    tpaList.Add(dllFile);
                }
            }
        }

        return string.Join(separator, tpaList);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }
}
