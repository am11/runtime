// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.AppHost;

/// <summary>
/// Application host - loads and runs a managed application.
/// </summary>
internal static unsafe class Program
{
    // Placeholder for the app binary path - this gets patched by "dotnet build" with the actual app DLL name.
    // The placeholder is SHA-256 of "foobar" and must be exactly 64 bytes (plus null terminator space).
    // The SDK searches for this byte sequence and replaces it.
    // Using a byte array ensures the exact bytes appear in the native binary.
    private static readonly byte[] s_appBinaryPathPlaceholder = "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"u8.ToArray();

    // Bundle header offset placeholder - 8 bytes for offset + 32 bytes signature
    // This gets patched by "dotnet publish" for single-file bundles
    private static readonly byte[] s_bundlePlaceholder =
    [
        // 8 bytes: bundle header offset (0 for non-bundle)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // 32 bytes: SHA-256 signature for ".net core bundle"
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    ];

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

    /// <summary>
    /// Gets the embedded app path from the placeholder (if patched).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? GetEmbeddedAppPath()
    {
        // Read the placeholder - if it's been patched, it will contain the app DLL name
        // The placeholder is "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2"
        // If still the placeholder, return null
        const string placeholder = "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";

        int nullIndex = Array.IndexOf(s_appBinaryPathPlaceholder, (byte)0);
        if (nullIndex < 0) nullIndex = s_appBinaryPathPlaceholder.Length;

        string value = Encoding.UTF8.GetString(s_appBinaryPathPlaceholder, 0, nullIndex);

        // Check if still placeholder
        if (value == placeholder)
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// Gets the bundle header offset from the placeholder (if patched).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long GetBundleHeaderOffset()
    {
        // First 8 bytes are the offset
        return BitConverter.ToInt64(s_bundlePlaceholder, 0);
    }

    private static int RunApp(string[] args)
    {
        string? exePath = Environment.ProcessPath;

        // Check if this is a single-file bundle
        long headerOffset = GetBundleHeaderOffset();
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
        // If we have an embedded app path (patched by SDK), use that
        string? embeddedPath = GetEmbeddedAppPath();
        if (!string.IsNullOrEmpty(embeddedPath))
        {
            appArgs = args;
            string executablePath = Environment.ProcessPath ?? "";
            string executableDir = Path.GetDirectoryName(executablePath) ?? ".";
            return Path.Combine(executableDir, embeddedPath);
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
