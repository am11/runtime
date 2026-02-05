// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.NetHost;

/// <summary>
/// Parameters for get_hostfxr_path.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GetHostFxrParameters
{
    /// <summary>
    /// Size of the struct. This is used for versioning.
    /// </summary>
    public nuint Size;

    /// <summary>
    /// Path to the component's assembly.
    /// If specified, hostfxr is located as if the assembly_path is the apphost.
    /// </summary>
    public char* AssemblyPath;

    /// <summary>
    /// Path to directory containing the dotnet executable.
    /// If specified, hostfxr is located as if an application is started using
    /// 'dotnet app.dll', which means it will be searched for under the dotnet_root
    /// path and the assembly_path is ignored.
    /// </summary>
    public char* DotnetRoot;
}

/// <summary>
/// Native exports for the nethost library.
/// </summary>
public static unsafe partial class NetHostExports
{
    /// <summary>
    /// Get the path to the hostfxr library.
    /// </summary>
    /// <param name="buffer">Buffer that will be populated with the hostfxr path, including a null terminator.</param>
    /// <param name="bufferSize">
    /// [in] Size of buffer in char units.
    /// [out] Size of buffer used in char units. If the input value is too small
    /// or buffer is null, this is populated with the minimum required size
    /// in char units for a buffer to hold the hostfxr path.
    /// </param>
    /// <param name="parameters">
    /// Optional. Parameters that modify the behaviour for locating the hostfxr library.
    /// If null, hostfxr is located using the environment variable or global registration.
    /// </param>
    /// <returns>0 on success, otherwise failure. 0x80008098 if buffer is too small.</returns>
    [UnmanagedCallersOnly(EntryPoint = "get_hostfxr_path")]
    public static int GetHostFxrPath(char* buffer, nuint* bufferSize, GetHostFxrParameters* parameters)
    {
        try
        {
            return GetHostFxrPathCore(buffer, bufferSize, parameters);
        }
        catch
        {
            return StatusCode.HostApiFailed.AsInt();
        }
    }

    private static int GetHostFxrPathCore(char* buffer, nuint* bufferSize, GetHostFxrParameters* parameters)
    {
        if (bufferSize is null)
        {
            return StatusCode.InvalidArgFailure.AsInt();
        }

        string? assemblyPath = null;
        string? dotnetRoot = null;

        if (parameters is not null)
        {
            // Validate the size field for versioning
            if (parameters->Size < (nuint)sizeof(GetHostFxrParameters))
            {
                return StatusCode.InvalidArgFailure.AsInt();
            }

            if (parameters->DotnetRoot is not null)
            {
                dotnetRoot = new string(parameters->DotnetRoot);
            }

            if (parameters->AssemblyPath is not null)
            {
                assemblyPath = new string(parameters->AssemblyPath);
            }
        }

        string? hostFxrPath = DotNetLocator.GetHostFxrPath(assemblyPath, dotnetRoot);
        if (hostFxrPath is null)
        {
            return StatusCode.CoreHostLibMissingFailure.AsInt();
        }

        // +1 for null terminator
        nuint requiredSize = (nuint)(hostFxrPath.Length + 1);

        if (buffer is null || *bufferSize < requiredSize)
        {
            *bufferSize = requiredSize;
            return StatusCode.HostApiBufferTooSmall.AsInt();
        }

        // Copy the path to the buffer
        ReadOnlySpan<char> pathSpan = hostFxrPath.AsSpan();
        Span<char> bufferSpan = new(buffer, (int)*bufferSize);
        pathSpan.CopyTo(bufferSpan);
        bufferSpan[pathSpan.Length] = '\0';

        *bufferSize = requiredSize;

        return StatusCode.Success.AsInt();
    }
}
