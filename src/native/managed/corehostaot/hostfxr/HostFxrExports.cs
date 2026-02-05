// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.HostFxr;

/// <summary>
/// Native exports for the hostfxr library.
/// </summary>
public static unsafe partial class HostFxrExports
{
    [ThreadStatic]
    private static delegate* unmanaged<char*, void> t_errorWriter;

    /// <summary>
    /// Sets a callback which is to be used to write errors to.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_set_error_writer")]
    public static delegate* unmanaged<char*, void> SetErrorWriter(delegate* unmanaged<char*, void> errorWriter)
    {
        var previous = t_errorWriter;
        t_errorWriter = errorWriter;

        return previous;
    }

    /// <summary>
    /// Initializes the hosting components using a .runtimeconfig.json file.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_initialize_for_runtime_config")]
    public static int InitializeForRuntimeConfig(
        char* runtimeConfigPath,
        HostFxrInitializeParameters* parameters,
        nint* hostContextHandle)
    {
        try
        {
            return InitializeForRuntimeConfigCore(runtimeConfigPath, parameters, hostContextHandle);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int InitializeForRuntimeConfigCore(
        char* runtimeConfigPath,
        HostFxrInitializeParameters* parameters,
        nint* hostContextHandle)
    {
        if (runtimeConfigPath is null || hostContextHandle is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        string configPath = new(runtimeConfigPath);
        string? hostPath = null;
        string? dotnetRoot = null;

        if (parameters is not null && parameters->Size >= (nuint)sizeof(HostFxrInitializeParameters))
        {
            if (parameters->HostPath is not null)
            {
                hostPath = new string(parameters->HostPath);
            }

            if (parameters->DotnetRoot is not null)
            {
                dotnetRoot = new string(parameters->DotnetRoot);
            }
        }

        var context = HostContext.CreateForRuntimeConfig(configPath, hostPath, dotnetRoot);
        if (context is null)
        {
            return StatusCode.InvalidConfigFile.AsInt();
        }

        *hostContextHandle = context.Handle;

        // Check if all frameworks were resolved
        if (context.RuntimeConfig is not null && context.RuntimeConfig.Frameworks.Count > 0)
        {
            bool allResolved = true;
            foreach (var fx in context.ResolvedFrameworks)
            {
                if (fx.ResolvedPath is null)
                {
                    allResolved = false;
                    break;
                }
            }

            if (!allResolved)
            {
                return StatusCode.FrameworkMissingFailure.AsInt();
            }
        }

        return StatusCode.Success.AsInt();
    }

    /// <summary>
    /// Initializes the hosting components for a dotnet command line running an application.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_initialize_for_dotnet_command_line")]
    public static int InitializeForDotnetCommandLine(
        int argc,
        char** argv,
        HostFxrInitializeParameters* parameters,
        nint* hostContextHandle)
    {
        try
        {
            return InitializeForDotnetCommandLineCore(argc, argv, parameters, hostContextHandle);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int InitializeForDotnetCommandLineCore(
        int argc,
        char** argv,
        HostFxrInitializeParameters* parameters,
        nint* hostContextHandle)
    {
        if (argc < 1 || argv is null || hostContextHandle is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        string[] args = new string[argc];
        for (int i = 0; i < argc; i++)
        {
            args[i] = new string(argv[i]);
        }

        string? hostPath = null;
        string? dotnetRoot = null;

        if (parameters is not null && parameters->Size >= (nuint)sizeof(HostFxrInitializeParameters))
        {
            if (parameters->HostPath is not null)
            {
                hostPath = new string(parameters->HostPath);
            }

            if (parameters->DotnetRoot is not null)
            {
                dotnetRoot = new string(parameters->DotnetRoot);
            }
        }

        var context = HostContext.CreateForCommandLine(args, hostPath, dotnetRoot);
        if (context is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        *hostContextHandle = context.Handle;

        return StatusCode.Success.AsInt();
    }

    /// <summary>
    /// Gets the runtime property value for an initialized host context.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_get_runtime_property_value")]
    public static int GetRuntimePropertyValue(
        nint hostContextHandle,
        char* name,
        char** value)
    {
        try
        {
            return GetRuntimePropertyValueCore(hostContextHandle, name, value);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int GetRuntimePropertyValueCore(nint hostContextHandle, char* name, char** value)
    {
        if (name is null || value is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        var context = HostContext.FromHandle(hostContextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        string propertyName = new(name);
        string? propertyValue = context.GetProperty(propertyName);

        if (propertyValue is null)
        {
            return StatusCode.HostPropertyNotFound.AsInt();
        }

        // Note: In a real implementation, we'd need to manage the lifetime of this string
        // For now, we allocate and the caller needs to understand the lifetime
        *value = (char*)Marshal.StringToHGlobalUni(propertyValue);

        return StatusCode.Success.AsInt();
    }

    /// <summary>
    /// Sets the value of a runtime property for an initialized host context.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_set_runtime_property_value")]
    public static int SetRuntimePropertyValue(
        nint hostContextHandle,
        char* name,
        char* value)
    {
        try
        {
            return SetRuntimePropertyValueCore(hostContextHandle, name, value);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int SetRuntimePropertyValueCore(nint hostContextHandle, char* name, char* value)
    {
        if (name is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        var context = HostContext.FromHandle(hostContextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        string propertyName = new(name);
        string? propertyValue = value is not null ? new string(value) : null;

        if (!context.SetProperty(propertyName, propertyValue))
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        return StatusCode.Success.AsInt();
    }

    /// <summary>
    /// Gets a typed delegate from the currently loaded CoreCLR or from a newly created one.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_get_runtime_delegate")]
    public static int GetRuntimeDelegate(
        nint hostContextHandle,
        HostFxrDelegateType type,
        void** @delegate)
    {
        try
        {
            return GetRuntimeDelegateCore(hostContextHandle, type, @delegate);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    // Static delegates to prevent GC collection
    private static delegate* unmanaged<char*, char*, char*, char*, void*, void**, int> s_loadAssemblyAndGetFunctionPointer;
    private static delegate* unmanaged<char*, char*, char*, void*, void*, void**, int> s_getFunctionPointer;
    private static delegate* unmanaged<char*, void*, void*, int> s_loadAssembly;
    private static delegate* unmanaged<void*, nuint, void*, nuint, void*, void*, int> s_loadAssemblyBytes;

    private static int GetRuntimeDelegateCore(nint hostContextHandle, HostFxrDelegateType type, void** @delegate)
    {
        if (@delegate is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        var context = HostContext.FromHandle(hostContextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        // Return the appropriate delegate based on type
        switch (type)
        {
            case HostFxrDelegateType.LoadAssemblyAndGetFunctionPointer:
                s_loadAssemblyAndGetFunctionPointer = &RuntimeDelegates.LoadAssemblyAndGetFunctionPointer;
                *@delegate = s_loadAssemblyAndGetFunctionPointer;
                return StatusCode.Success.AsInt();

            case HostFxrDelegateType.GetFunctionPointer:
                s_getFunctionPointer = &RuntimeDelegates.GetFunctionPointer;
                *@delegate = s_getFunctionPointer;
                return StatusCode.Success.AsInt();

            case HostFxrDelegateType.LoadAssembly:
                s_loadAssembly = &RuntimeDelegates.LoadAssembly;
                *@delegate = s_loadAssembly;
                return StatusCode.Success.AsInt();

            case HostFxrDelegateType.LoadAssemblyBytes:
                s_loadAssemblyBytes = &RuntimeDelegates.LoadAssemblyBytes;
                *@delegate = s_loadAssemblyBytes;
                return StatusCode.Success.AsInt();

            default:
                return StatusCode.HostApiUnsupportedScenario.AsInt();
        }
    }

    /// <summary>
    /// Load CoreCLR and run the application for an initialized host context.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_run_app")]
    public static int RunApp(nint hostContextHandle)
    {
        try
        {
            return RunAppCore(hostContextHandle);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int RunAppCore(nint hostContextHandle)
    {
        var context = HostContext.FromHandle(hostContextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        if (!context.IsForCommandLine)
        {
            return StatusCode.HostApiUnsupportedScenario.AsInt();
        }

        // Get the app path from context
        string? appPath = context.GetProperty("APP_PATH");
        if (string.IsNullOrEmpty(appPath))
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        // Get resolved frameworks
        var resolvedFrameworks = context.ResolvedFrameworks;
        if (resolvedFrameworks.Count == 0)
        {
            return StatusCode.FrameworkMissingFailure.AsInt();
        }

        // Get deps file
        string? depsFile = System.IO.Path.ChangeExtension(appPath, ".deps.json");
        if (!System.IO.File.Exists(depsFile))
        {
            depsFile = null;
        }

        // Create runtime host and execute
        using var host = RuntimeHost.Create(
            resolvedFrameworks,
            appPath,
            depsFile,
            null);

        if (host is null)
        {
            return StatusCode.CoreClrResolveFailure.AsInt();
        }

        // Get command line args from context
        string[] args = context.CommandLineArgs ?? [];

        var result = host.ExecuteAssembly(appPath, args, out int exitCode);
        if (result != StatusCode.Success)
        {
            return result.AsInt();
        }

        return exitCode;
    }

    /// <summary>
    /// Closes an initialized host context.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_close")]
    public static int Close(nint hostContextHandle)
    {
        try
        {
            return CloseCore(hostContextHandle);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int CloseCore(nint hostContextHandle)
    {
        var context = HostContext.FromHandle(hostContextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        context.Dispose();

        return StatusCode.Success.AsInt();
    }

    /// <summary>
    /// Returns available SDKs and frameworks.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hostfxr_get_dotnet_environment_info")]
    public static int GetDotnetEnvironmentInfo(
        char* dotnetRoot,
        void* reserved,
        delegate* unmanaged<HostFxrDotnetEnvironmentInfo*, void*, void> result,
        void* resultContext)
    {
        try
        {
            return GetDotnetEnvironmentInfoCore(dotnetRoot, reserved, result, resultContext);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int GetDotnetEnvironmentInfoCore(
        char* dotnetRoot,
        void* reserved,
        delegate* unmanaged<HostFxrDotnetEnvironmentInfo*, void*, void> result,
        void* resultContext)
    {
        _ = reserved; // Reserved for future use

        if (result is null)
        {
            return StatusCode.Success.AsInt();
        }

        string? root = dotnetRoot is not null ? new string(dotnetRoot) : DotNetLocator.GetDefaultDotNetRoot();

        if (string.IsNullOrEmpty(root))
        {
            // No dotnet installation found, return empty info
            var emptyInfo = new HostFxrDotnetEnvironmentInfo
            {
                Size = (nuint)sizeof(HostFxrDotnetEnvironmentInfo),
            };
            result(&emptyInfo, resultContext);

            return StatusCode.Success.AsInt();
        }

        var resolver = new FrameworkResolver(root);
        var installedSdks = resolver.GetInstalledSdks();
        var installedFrameworks = resolver.GetInstalledFrameworks();

        // Allocate arrays for SDKs and frameworks
        var sdkInfos = new HostFxrDotnetEnvironmentSdkInfo[installedSdks.Count];
        var frameworkInfos = new HostFxrDotnetEnvironmentFrameworkInfo[installedFrameworks.Count];

        // We need to pin strings for the callback - use a simple approach with fixed arrays
        nint[]? sdkVersionPtrs = installedSdks.Count > 0 ? new nint[installedSdks.Count] : null;
        nint[]? sdkPathPtrs = installedSdks.Count > 0 ? new nint[installedSdks.Count] : null;
        nint[]? fxNamePtrs = installedFrameworks.Count > 0 ? new nint[installedFrameworks.Count] : null;
        nint[]? fxVersionPtrs = installedFrameworks.Count > 0 ? new nint[installedFrameworks.Count] : null;
        nint[]? fxPathPtrs = installedFrameworks.Count > 0 ? new nint[installedFrameworks.Count] : null;

        try
        {
            // Populate SDK infos
            for (int i = 0; i < installedSdks.Count; i++)
            {
                var (version, path) = installedSdks[i];
                sdkVersionPtrs![i] = Marshal.StringToHGlobalUni(version);
                sdkPathPtrs![i] = Marshal.StringToHGlobalUni(path);
                sdkInfos[i] = new HostFxrDotnetEnvironmentSdkInfo
                {
                    Size = (nuint)sizeof(HostFxrDotnetEnvironmentSdkInfo),
                    Version = (char*)sdkVersionPtrs[i],
                    Path = (char*)sdkPathPtrs[i],
                };
            }

            // Populate framework infos
            for (int i = 0; i < installedFrameworks.Count; i++)
            {
                var (name, version, path) = installedFrameworks[i];
                fxNamePtrs![i] = Marshal.StringToHGlobalUni(name);
                fxVersionPtrs![i] = Marshal.StringToHGlobalUni(version);
                fxPathPtrs![i] = Marshal.StringToHGlobalUni(path);
                frameworkInfos[i] = new HostFxrDotnetEnvironmentFrameworkInfo
                {
                    Size = (nuint)sizeof(HostFxrDotnetEnvironmentFrameworkInfo),
                    Name = (char*)fxNamePtrs[i],
                    Version = (char*)fxVersionPtrs[i],
                    Path = (char*)fxPathPtrs[i],
                };
            }

            fixed (HostFxrDotnetEnvironmentSdkInfo* sdksPtr = sdkInfos)
            fixed (HostFxrDotnetEnvironmentFrameworkInfo* frameworksPtr = frameworkInfos)
            {
                // Version is embedded at build time
                var info = new HostFxrDotnetEnvironmentInfo
                {
                    Size = (nuint)sizeof(HostFxrDotnetEnvironmentInfo),
                    HostFxrVersion = null,
                    HostFxrCommitHash = null,
                    SdkCount = (nuint)installedSdks.Count,
                    Sdks = sdksPtr,
                    FrameworkCount = (nuint)installedFrameworks.Count,
                    Frameworks = frameworksPtr,
                };

                result(&info, resultContext);
            }
        }
        finally
        {
            // Free allocated strings
            if (sdkVersionPtrs is not null)
            {
                for (int i = 0; i < sdkVersionPtrs.Length; i++)
                {
                    if (sdkVersionPtrs[i] != 0) Marshal.FreeHGlobal(sdkVersionPtrs[i]);
                    if (sdkPathPtrs![i] != 0) Marshal.FreeHGlobal(sdkPathPtrs[i]);
                }
            }

            if (fxNamePtrs is not null)
            {
                for (int i = 0; i < fxNamePtrs.Length; i++)
                {
                    if (fxNamePtrs[i] != 0) Marshal.FreeHGlobal(fxNamePtrs[i]);
                    if (fxVersionPtrs![i] != 0) Marshal.FreeHGlobal(fxVersionPtrs[i]);
                    if (fxPathPtrs![i] != 0) Marshal.FreeHGlobal(fxPathPtrs[i]);
                }
            }
        }

        return StatusCode.Success.AsInt();
    }
}
