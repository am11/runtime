// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.HostPolicy;

/// <summary>
/// Native exports for the hostpolicy library.
/// </summary>
public static unsafe partial class HostPolicyExports
{
    [ThreadStatic]
    private static delegate* unmanaged<char*, void> t_errorWriter;

    /// <summary>
    /// Sets a callback which is to be used to write errors to.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "corehost_set_error_writer")]
    public static delegate* unmanaged<char*, void> SetErrorWriter(delegate* unmanaged<char*, void> errorWriter)
    {
        var previous = t_errorWriter;
        t_errorWriter = errorWriter;

        return previous;
    }

    /// <summary>
    /// Initialize hostpolicy with the given request.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "corehost_initialize")]
    public static int Initialize(
        CoreHostInitializeRequest* initRequest,
        CoreHostInitializeFlags options,
        CoreHostContextContract* context)
    {
        try
        {
            return InitializeCore(initRequest, options, context);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int InitializeCore(
        CoreHostInitializeRequest* initRequest,
        CoreHostInitializeFlags options,
        CoreHostContextContract* context)
    {
        if (initRequest is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        string? hostPath = null;
        string? dotnetRoot = null;
        (string Key, string Value)[] properties = [];

        if (initRequest->Size >= (nuint)sizeof(CoreHostInitializeRequest))
        {
            if (initRequest->HostPath is not null)
            {
                hostPath = new string(initRequest->HostPath);
            }

            if (initRequest->DotnetRoot is not null)
            {
                dotnetRoot = new string(initRequest->DotnetRoot);
            }

            if (initRequest->PropertyCount > 0 && initRequest->PropertyKeys is not null && initRequest->PropertyValues is not null)
            {
                properties = new (string, string)[(int)initRequest->PropertyCount];
                for (int i = 0; i < (int)initRequest->PropertyCount; i++)
                {
                    properties[i] = (new string(initRequest->PropertyKeys[i]), new string(initRequest->PropertyValues[i]));
                }
            }
        }

        var policyContext = PolicyContext.Create(hostPath, dotnetRoot, properties);

        if (context is not null && (options & CoreHostInitializeFlags.GetContract) != 0)
        {
            context->Size = (nuint)sizeof(CoreHostContextContract);
            context->Version = 0;
            context->ContextHandle = policyContext.Handle;
            context->GetRuntimePropertyValue = &GetRuntimePropertyValueCallback;
            context->SetRuntimePropertyValue = &SetRuntimePropertyValueCallback;
            context->GetRuntimeProperties = &GetRuntimePropertiesCallback;
            context->LoadRuntime = &LoadRuntimeCallback;
            context->RunApp = &RunAppCallback;
            context->GetRuntimeDelegate = &GetRuntimeDelegateCallback;
        }

        return StatusCode.Success.AsInt();
    }

    [UnmanagedCallersOnly]
    private static int GetRuntimePropertyValueCallback(nint contextHandle, char* name, char** value)
    {
        if (name is null || value is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        var context = PolicyContext.FromHandle(contextHandle);
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

        *value = (char*)Marshal.StringToHGlobalUni(propertyValue);

        return StatusCode.Success.AsInt();
    }

    [UnmanagedCallersOnly]
    private static int SetRuntimePropertyValueCallback(nint contextHandle, char* name, char* value)
    {
        if (name is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        var context = PolicyContext.FromHandle(contextHandle);
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

    [UnmanagedCallersOnly]
    private static int GetRuntimePropertiesCallback(nint contextHandle, nuint* count, char** keys, char** values)
    {
        if (count is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        var context = PolicyContext.FromHandle(contextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        var (propKeys, propValues) = context.GetAllProperties();
        nuint requiredCount = (nuint)propKeys.Length;

        if (keys is null || values is null || *count < requiredCount)
        {
            *count = requiredCount;
            return StatusCode.HostApiBufferTooSmall.AsInt();
        }

        for (int i = 0; i < propKeys.Length; i++)
        {
            keys[i] = (char*)Marshal.StringToHGlobalUni(propKeys[i]);
            values[i] = (char*)Marshal.StringToHGlobalUni(propValues[i]);
        }

        *count = requiredCount;

        return StatusCode.Success.AsInt();
    }

    [UnmanagedCallersOnly]
    private static int LoadRuntimeCallback(nint contextHandle)
    {
        var context = PolicyContext.FromHandle(contextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        if (context.IsRuntimeLoaded)
        {
            return StatusCode.Success_HostAlreadyInitialized.AsInt();
        }

        // Need app path and deps file from properties
        string? appPath = context.GetProperty("APP_PATH");
        string? depsFile = context.GetProperty(RuntimePropertyNames.AppContextDepsFiles);

        if (string.IsNullOrEmpty(appPath))
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        var result = context.LoadRuntime(appPath, depsFile);

        return result.AsInt();
    }

    [UnmanagedCallersOnly]
    private static int RunAppCallback(nint contextHandle, int argc, void** argv)
    {
        var context = PolicyContext.FromHandle(contextHandle);
        if (context is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        // Get app path
        string? appPath = context.GetProperty("APP_PATH");
        if (string.IsNullOrEmpty(appPath))
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        // Build args array
        string[] args = new string[argc > 0 ? argc - 1 : 0];
        if (argv is not null && argc > 1)
        {
            for (int i = 1; i < argc; i++)
            {
                args[i - 1] = new string((char*)argv[i]);
            }
        }

        var result = context.ExecuteAssembly(appPath, args, out int exitCode);
        if (result != StatusCode.Success)
        {
            return result.AsInt();
        }

        return exitCode;
    }

    [UnmanagedCallersOnly]
    private static int GetRuntimeDelegateCallback(nint contextHandle, int delegateType, void** delegateOut)
    {
        var context = PolicyContext.FromHandle(contextHandle);
        if (context is null || delegateOut is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        var type = (HostFxrDelegateType)delegateType;

        switch (type)
        {
            case HostFxrDelegateType.LoadAssemblyAndGetFunctionPointer:
                *delegateOut = (delegate* unmanaged<char*, char*, char*, char*, void*, void**, int>)&RuntimeDelegates.LoadAssemblyAndGetFunctionPointer;
                return StatusCode.Success.AsInt();

            case HostFxrDelegateType.GetFunctionPointer:
                *delegateOut = (delegate* unmanaged<char*, char*, char*, void*, void*, void**, int>)&RuntimeDelegates.GetFunctionPointer;
                return StatusCode.Success.AsInt();

            case HostFxrDelegateType.LoadAssembly:
                *delegateOut = (delegate* unmanaged<char*, void*, void*, int>)&RuntimeDelegates.LoadAssembly;
                return StatusCode.Success.AsInt();

            case HostFxrDelegateType.LoadAssemblyBytes:
                *delegateOut = (delegate* unmanaged<void*, nuint, void*, nuint, void*, void*, int>)&RuntimeDelegates.LoadAssemblyBytes;
                return StatusCode.Success.AsInt();

            default:
                return StatusCode.HostApiUnsupportedScenario.AsInt();
        }
    }

    /// <summary>
    /// Resolve component dependencies for the given assembly.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "corehost_resolve_component_dependencies")]
    public static int ResolveComponentDependencies(
        char* componentMainAssemblyPath,
        delegate* unmanaged<char*, char*, char*, void> result)
    {
        try
        {
            return ResolveComponentDependenciesCore(componentMainAssemblyPath, result);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int ResolveComponentDependenciesCore(
        char* componentMainAssemblyPath,
        delegate* unmanaged<char*, char*, char*, void> result)
    {
        if (componentMainAssemblyPath is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        string componentPath = new(componentMainAssemblyPath);
        string componentDir = System.IO.Path.GetDirectoryName(componentPath) ?? ".";
        string depsPath = System.IO.Path.ChangeExtension(componentPath, ".deps.json");

        string assemblyPaths = "";
        string nativePaths = "";
        string resourcePaths = "";

        if (System.IO.File.Exists(depsPath))
        {
            var deps = DepsJson.Parse(depsPath);
            if (deps is not null && deps.IsValid)
            {
                char separator = OperatingSystem.IsWindows() ? ';' : ':';

                // Build assembly paths
                var assemblies = new System.Collections.Generic.List<string>();
                foreach (string path in deps.GetAssemblyPaths(componentDir))
                {
                    if (System.IO.File.Exists(path))
                    {
                        assemblies.Add(path);
                    }
                }
                assemblyPaths = string.Join(separator, assemblies);

                // Build native paths - just return the component directory
                nativePaths = componentDir;

                // Build resource paths - just return the component directory
                resourcePaths = componentDir;
            }
        }

        if (result is not null)
        {
            fixed (char* assemblyPathsPtr = assemblyPaths)
            fixed (char* nativePathsPtr = nativePaths)
            fixed (char* resourcePathsPtr = resourcePaths)
            {
                result(assemblyPathsPtr, nativePathsPtr, resourcePathsPtr);
            }
        }

        return StatusCode.Success.AsInt();
    }

    /// <summary>
    /// Main entry point for running an application.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "corehost_main")]
    public static int Main(int argc, char** argv)
    {
        try
        {
            return MainCore(argc, argv, null, 0, null);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    /// <summary>
    /// Main entry point for running an application with output buffer.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "corehost_main_with_output_buffer")]
    public static int MainWithOutputBuffer(int argc, char** argv, char* buffer, int bufferSize, int* requiredBufferSize)
    {
        try
        {
            return MainCore(argc, argv, buffer, bufferSize, requiredBufferSize);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int MainCore(int argc, char** argv, char* buffer, int bufferSize, int* requiredBufferSize)
    {
        if (argc < 1 || argv is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        // Get the app path from argv[0]
        string appPath = new(argv[0]);

        // Build args array
        string[] args = new string[argc];
        for (int i = 0; i < argc; i++)
        {
            args[i] = new string(argv[i]);
        }

        // Find dotnet root
        string? dotnetRoot = DotNetLocator.GetDefaultDotNetRoot();
        if (string.IsNullOrEmpty(dotnetRoot))
        {
            return StatusCode.CoreHostLibMissingFailure.AsInt();
        }

        // Find the runtime config
        string configPath = System.IO.Path.ChangeExtension(appPath, ".runtimeconfig.json");
        var config = RuntimeConfig.Parse(configPath);
        if (config is null || !config.IsValid)
        {
            return StatusCode.InvalidConfigFile.AsInt();
        }

        // Resolve frameworks
        var resolver = new FrameworkResolver(dotnetRoot);
        var resolvedFrameworks = new System.Collections.Generic.List<FrameworkReference>();
        if (!resolver.ResolveFrameworks(config, resolvedFrameworks))
        {
            return StatusCode.FrameworkMissingFailure.AsInt();
        }

        // Find deps.json
        string? depsFile = System.IO.Path.ChangeExtension(appPath, ".deps.json");
        if (!System.IO.File.Exists(depsFile))
        {
            depsFile = null;
        }

        // Create runtime host
        using var host = RuntimeHost.Create(
            resolvedFrameworks,
            appPath,
            depsFile,
            config.Properties as System.Collections.Generic.IDictionary<string, string>);

        if (host is null)
        {
            return StatusCode.CoreClrResolveFailure.AsInt();
        }

        // Execute the assembly
        var result = host.ExecuteAssembly(appPath, args[1..], out int exitCode);
        if (result != StatusCode.Success)
        {
            return result.AsInt();
        }

        // If output buffer requested, we would capture stdout here
        // For now, just return success
        if (buffer is not null && requiredBufferSize is not null)
        {
            *requiredBufferSize = 0;
        }

        return exitCode;
    }
}
