// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Types of runtime delegates that can be requested.
/// </summary>
public enum HostFxrDelegateType
{
    ComActivation,
    LoadInMemoryAssembly,
    WinRTActivation,
    ComRegister,
    ComUnregister,
    LoadAssemblyAndGetFunctionPointer,
    GetFunctionPointer,
    LoadAssembly,
    LoadAssemblyBytes,
}
