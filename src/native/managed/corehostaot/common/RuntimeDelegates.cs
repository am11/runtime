// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Provides runtime delegate implementations for hosting scenarios.
/// These delegates are obtained from the loaded CoreCLR runtime.
/// </summary>
internal static unsafe class RuntimeDelegates
{
    /// <summary>
    /// Sentinel value indicating UnmanagedCallersOnly method.
    /// </summary>
    public static readonly nint UnmanagedCallersOnlyMethod = -1;

    // Function pointers obtained from CoreCLR
    private static delegate* unmanaged<byte*, byte*, byte*, byte*, void*, void**, int> s_loadAssemblyAndGetFunctionPointerFromCoreclr;
    private static delegate* unmanaged<byte*, byte*, byte*, void*, void*, void**, int> s_getFunctionPointerFromCoreclr;

    /// <summary>
    /// Sets the load_assembly_and_get_function_pointer delegate obtained from CoreCLR.
    /// </summary>
    public static void SetLoadAssemblyAndGetFunctionPointer(nint functionPointer)
    {
        s_loadAssemblyAndGetFunctionPointerFromCoreclr =
            (delegate* unmanaged<byte*, byte*, byte*, byte*, void*, void**, int>)functionPointer;
    }

    /// <summary>
    /// Sets the get_function_pointer delegate obtained from CoreCLR.
    /// </summary>
    public static void SetGetFunctionPointer(nint functionPointer)
    {
        s_getFunctionPointerFromCoreclr =
            (delegate* unmanaged<byte*, byte*, byte*, void*, void*, void**, int>)functionPointer;
    }

    /// <summary>
    /// Loads an assembly and gets a function pointer to a method.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int LoadAssemblyAndGetFunctionPointer(
        char* assemblyPath,
        char* typeName,
        char* methodName,
        char* delegateTypeName,
        void* reserved,
        void** delegateOut)
    {
        if (s_loadAssemblyAndGetFunctionPointerFromCoreclr is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        if (assemblyPath is null || typeName is null || methodName is null || delegateOut is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        return s_loadAssemblyAndGetFunctionPointerFromCoreclr(
            (byte*)assemblyPath,
            (byte*)typeName,
            (byte*)methodName,
            (byte*)delegateTypeName,
            reserved,
            delegateOut);
    }

    /// <summary>
    /// Gets a function pointer to a method from an already loaded assembly.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int GetFunctionPointer(
        char* typeName,
        char* methodName,
        char* delegateTypeName,
        void* loadContext,
        void* reserved,
        void** delegateOut)
    {
        if (s_getFunctionPointerFromCoreclr is null)
        {
            return StatusCode.HostInvalidState.AsInt();
        }

        if (typeName is null || methodName is null || delegateOut is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        return s_getFunctionPointerFromCoreclr(
            (byte*)typeName,
            (byte*)methodName,
            (byte*)delegateTypeName,
            loadContext,
            reserved,
            delegateOut);
    }

    /// <summary>
    /// Loads an assembly into the default load context.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int LoadAssembly(
        char* assemblyPath,
        void* loadContext,
        void* reserved)
    {
        _ = assemblyPath;
        _ = loadContext;
        _ = reserved;

        return StatusCode.HostApiUnsupportedScenario.AsInt();
    }

    /// <summary>
    /// Loads an assembly from a byte array.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int LoadAssemblyBytes(
        void* assemblyBytes,
        nuint assemblyBytesLen,
        void* symbolsBytes,
        nuint symbolsBytesLen,
        void* loadContext,
        void* reserved)
    {
        _ = assemblyBytes;
        _ = assemblyBytesLen;
        _ = symbolsBytes;
        _ = symbolsBytesLen;
        _ = loadContext;
        _ = reserved;

        return StatusCode.HostApiUnsupportedScenario.AsInt();
    }
}
