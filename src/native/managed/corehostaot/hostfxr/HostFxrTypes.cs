// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.HostFxr;

/// <summary>
/// Parameters for hostfxr initialization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HostFxrInitializeParameters
{
    public nuint Size;
    public char* HostPath;
    public char* DotnetRoot;
}

/// <summary>
/// SDK information for dotnet environment enumeration.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HostFxrDotnetEnvironmentSdkInfo
{
    public nuint Size;
    public char* Version;
    public char* Path;
}

/// <summary>
/// Framework information for dotnet environment enumeration.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HostFxrDotnetEnvironmentFrameworkInfo
{
    public nuint Size;
    public char* Name;
    public char* Version;
    public char* Path;
}

/// <summary>
/// Complete dotnet environment information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HostFxrDotnetEnvironmentInfo
{
    public nuint Size;
    public char* HostFxrVersion;
    public char* HostFxrCommitHash;
    public nuint SdkCount;
    public HostFxrDotnetEnvironmentSdkInfo* Sdks;
    public nuint FrameworkCount;
    public HostFxrDotnetEnvironmentFrameworkInfo* Frameworks;
}

/// <summary>
/// Framework resolution result.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HostFxrFrameworkResult
{
    public nuint Size;
    public char* Name;
    public char* RequestedVersion;
    public char* ResolvedVersion;
    public char* ResolvedPath;
}

/// <summary>
/// Result of framework resolution.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HostFxrResolveFrameworksResult
{
    public nuint Size;
    public nuint ResolvedCount;
    public HostFxrFrameworkResult* ResolvedFrameworks;
    public nuint UnresolvedCount;
    public HostFxrFrameworkResult* UnresolvedFrameworks;
}
