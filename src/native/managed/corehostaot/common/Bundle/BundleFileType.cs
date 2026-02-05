// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Types of files that can be embedded in a bundle.
/// </summary>
public enum BundleFileType : byte
{
    Unknown,
    Assembly,
    NativeBinary,
    DepsJson,
    RuntimeConfigJson,
    Symbols,
}
