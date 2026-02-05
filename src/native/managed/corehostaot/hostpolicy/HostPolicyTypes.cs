// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.HostPolicy;

/// <summary>
/// Initialization request for corehost_initialize.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CoreHostInitializeRequest
{
    public nuint Size;
    public uint Version;
    public char** PropertyKeys;
    public char** PropertyValues;
    public nuint PropertyCount;
    public char* HostPath;
    public char* DotnetRoot;
}

/// <summary>
/// Context contract returned by corehost_initialize.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CoreHostContextContract
{
    public nuint Size;
    public uint Version;
    public nint ContextHandle;
    public delegate* unmanaged<nint, char*, char**, int> GetRuntimePropertyValue;
    public delegate* unmanaged<nint, char*, char*, int> SetRuntimePropertyValue;
    public delegate* unmanaged<nint, nuint*, char**, char**, int> GetRuntimeProperties;
    public delegate* unmanaged<nint, int> LoadRuntime;
    public delegate* unmanaged<nint, int, void**, int> RunApp;
    public delegate* unmanaged<nint, int, void**, int> GetRuntimeDelegate;
}

/// <summary>
/// Options for corehost_initialize.
/// </summary>
[Flags]
public enum CoreHostInitializeFlags : uint
{
    None = 0,
    WaitForInitialized = 1,
    GetContract = 2,
}
