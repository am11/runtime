// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.AppHost;

/// <summary>
/// Application host - loads and runs a managed application.
/// </summary>
internal static unsafe class Program
{
    // This can be embedded/patched by the build process
    // CS0649: Field is never assigned - intentional, will be patched at build time
#pragma warning disable CS0649
    private static readonly string? EmbeddedAppPath;
    private static readonly long BundleHeaderOffset;
#pragma warning restore CS0649

    public static int Main(string[] args)
    {
        try
        {
            return RunApp(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int RunApp(string[] args)
    {
        string? exePath = Environment.ProcessPath;

        // Check if this is a single-file bundle
        long headerOffset = BundleHeaderOffset;
        if (headerOffset == 0 && !string.IsNullOrEmpty(exePath))
        {
            headerOffset = BundleExtractor.GetBundleHeaderOffset(exePath);
        }

        if (headerOffset != 0 && !string.IsNullOrEmpty(exePath))
        {
            return RunFromBundle(exePath, headerOffset, args);
        }

        // Not a bundle - run as normal apphost
        return RunFromDisk(args);
    }

    private static int RunFromBundle(string bundlePath, long headerOffset, string[] args)
    {
        // Initialize bundle support
        if (!BundleProbe.Initialize(bundlePath, headerOffset))
        {
            Console.Error.WriteLine("Failed to read bundle.");
            return StatusCode.BundleExtractionFailure.AsInt();
        }

        using var bundleProbe = BundleProbe.Current!;

        // Get runtime config from bundle
        var config = bundleProbe.ParseRuntimeConfig();
        if (config is null || !config.IsValid)
        {
            Console.Error.WriteLine("Failed to parse runtime config from bundle.");
            return StatusCode.InvalidConfigFile.AsInt();
        }

        // Find dotnet root
        string? dotnetRoot = DotNetLocator.GetDefaultDotNetRoot();
        if (string.IsNullOrEmpty(dotnetRoot))
        {
            Console.Error.WriteLine("Could not find .NET installation.");
            return StatusCode.CoreHostLibMissingFailure.AsInt();
        }

        // Resolve frameworks
        var resolver = new FrameworkResolver(dotnetRoot);
        var resolvedFrameworks = new System.Collections.Generic.List<FrameworkReference>();
        if (!resolver.ResolveFrameworks(config, resolvedFrameworks))
        {
            Console.Error.WriteLine("Failed to resolve required frameworks.");
            return StatusCode.FrameworkMissingFailure.AsInt();
        }

        // Determine app path - use extraction dir or bundle path
        string appPath = bundleProbe.ExtractionDirectory;
        string? appDll = FindAppDll(bundleProbe.Manifest);
        if (!string.IsNullOrEmpty(appDll))
        {
            appPath = Path.Combine(bundleProbe.ExtractionDirectory, appDll);
        }

        // Create runtime host
        using var host = RuntimeHost.Create(
            resolvedFrameworks,
            appPath,
            depsFile: null, // deps.json is in the bundle
            config.Properties as System.Collections.Generic.IDictionary<string, string>);

        if (host is null)
        {
            Console.Error.WriteLine("Failed to create runtime host.");
            return StatusCode.CoreClrResolveFailure.AsInt();
        }

        // Initialize and run
        var initResult = host.Initialize(appPath);
        if (initResult != StatusCode.Success)
        {
            Console.Error.WriteLine($"Failed to initialize runtime: {initResult}");
            return initResult.AsInt();
        }

        var execResult = host.ExecuteAssembly(appPath, args, out int exitCode);
        if (execResult != StatusCode.Success)
        {
            Console.Error.WriteLine($"Failed to execute assembly: {execResult}");
            return execResult.AsInt();
        }

        return exitCode;
    }

    private static string? FindAppDll(BundleManifest manifest)
    {
        // Find the main assembly (first .dll that's not a native binary)
        foreach (var entry in manifest.Files)
        {
            if (entry.Type == BundleFileType.Assembly &&
                entry.RelativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return entry.RelativePath;
            }
        }

        return null;
    }

    private static int RunFromDisk(string[] args)
    {
        // Determine the app to run
        string? appPath = GetAppPath(args, out string[] appArgs);
        if (string.IsNullOrEmpty(appPath))
        {
            Console.Error.WriteLine("Could not determine the application to run.");
            return StatusCode.AppHostExeNotBoundFailure.AsInt();
        }

        if (!File.Exists(appPath))
        {
            Console.Error.WriteLine($"Application '{appPath}' not found.");
            return StatusCode.AppPathFindFailure.AsInt();
        }

        // Find the runtime config
        string configPath = Path.ChangeExtension(appPath, ".runtimeconfig.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Runtime config '{configPath}' not found.");
            return StatusCode.InvalidConfigFile.AsInt();
        }

        // Parse the runtime config
        var config = RuntimeConfig.Parse(configPath);
        if (config is null || !config.IsValid)
        {
            Console.Error.WriteLine($"Failed to parse runtime config '{configPath}'.");
            return StatusCode.InvalidConfigFile.AsInt();
        }

        // Find dotnet root
        string? dotnetRoot = DotNetLocator.GetDefaultDotNetRoot();
        if (string.IsNullOrEmpty(dotnetRoot))
        {
            Console.Error.WriteLine("Could not find .NET installation.");
            return StatusCode.CoreHostLibMissingFailure.AsInt();
        }

        // Resolve frameworks
        var resolver = new FrameworkResolver(dotnetRoot);
        var resolvedFrameworks = new System.Collections.Generic.List<FrameworkReference>();
        if (!resolver.ResolveFrameworks(config, resolvedFrameworks))
        {
            Console.Error.WriteLine("Failed to resolve required frameworks.");
            foreach (var fx in config.Frameworks)
            {
                if (fx.ResolvedPath is null)
                {
                    Console.Error.WriteLine($"  - {fx.Name} {fx.Version}");
                }
            }
            return StatusCode.FrameworkMissingFailure.AsInt();
        }

        // Find deps.json
        string? depsFile = Path.ChangeExtension(appPath, ".deps.json");
        if (!File.Exists(depsFile))
        {
            depsFile = null;
        }

        // Create runtime host
        using var host = RuntimeHost.Create(resolvedFrameworks, appPath, depsFile, config.Properties as System.Collections.Generic.IDictionary<string, string>);
        if (host is null)
        {
            Console.Error.WriteLine("Failed to create runtime host.");
            return StatusCode.CoreClrResolveFailure.AsInt();
        }

        // Initialize and run
        var initResult = host.Initialize(appPath);
        if (initResult != StatusCode.Success)
        {
            Console.Error.WriteLine($"Failed to initialize runtime: {initResult}");
            return initResult.AsInt();
        }

        var execResult = host.ExecuteAssembly(appPath, appArgs, out int exitCode);
        if (execResult != StatusCode.Success)
        {
            Console.Error.WriteLine($"Failed to execute assembly: {execResult}");
            return execResult.AsInt();
        }

        return exitCode;
    }

    private static string? GetAppPath(string[] args, out string[] appArgs)
    {
        // If we have an embedded app path, use that
        if (!string.IsNullOrEmpty(EmbeddedAppPath))
        {
            appArgs = args;
            string executablePath = Environment.ProcessPath ?? "";
            string executableDir = Path.GetDirectoryName(executablePath) ?? ".";
            return Path.Combine(executableDir, EmbeddedAppPath);
        }

        // Otherwise, look for a .dll with the same name as the executable
        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            string dllPath = Path.ChangeExtension(exePath, ".dll");
            if (File.Exists(dllPath))
            {
                appArgs = args;
                return dllPath;
            }
        }

        // Fall back to first argument
        if (args.Length > 0 && args[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            appArgs = args.Length > 1 ? args[1..] : [];
            return args[0];
        }

        appArgs = args;
        return null;
    }
}
