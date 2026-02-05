// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Status codes returned by hosting APIs.
/// These correspond to the error codes defined in error_codes.h
/// </summary>
public enum StatusCode : uint
{
    // Success
    Success = 0,
    Success_HostAlreadyInitialized = 0x00000001,
    Success_DifferentRuntimeProperties = 0x00000002,

    // Failure
    InvalidArgFailure = 0x80008081,
    CoreHostLibLoadFailure = 0x80008082,
    CoreHostLibMissingFailure = 0x80008083,
    CoreHostEntryPointFailure = 0x80008084,
    CurrentHostFindFailure = 0x80008085,
    CoreClrResolveFailure = 0x80008087,
    CoreClrBindFailure = 0x80008088,
    CoreClrInitFailure = 0x80008089,
    CoreClrExeFailure = 0x8000808a,
    ResolverInitFailure = 0x8000808b,
    ResolverResolveFailure = 0x8000808c,
    LibHostInitFailure = 0x8000808e,
    LibHostInvalidArgs = 0x80008092,
    InvalidConfigFile = 0x80008093,
    AppArgNotRunnable = 0x80008094,
    AppHostExeNotBoundFailure = 0x80008095,
    FrameworkMissingFailure = 0x80008096,
    HostApiFailed = 0x80008097,
    HostApiBufferTooSmall = 0x80008098,
    AppPathFindFailure = 0x8000809a,
    SdkResolveFailure = 0x8000809b,
    FrameworkCompatFailure = 0x8000809c,
    FrameworkCompatRetry = 0x8000809d,
    BundleExtractionFailure = 0x8000809f,
    BundleExtractionIOError = 0x800080a0,
    LibHostDuplicateProperty = 0x800080a1,
    HostApiUnsupportedVersion = 0x800080a2,
    HostInvalidState = 0x800080a3,
    HostPropertyNotFound = 0x800080a4,
    HostIncompatibleConfig = 0x800080a5,
    HostApiUnsupportedScenario = 0x800080a6,
    HostFeatureDisabled = 0x800080a7,
}

public static class StatusCodeExtensions
{
    public static bool Succeeded(this StatusCode code) => (uint)code < 0x80000000;

    /// <summary>
    /// Converts the status code to an int for returning from native exports.
    /// </summary>
    public static int AsInt(this StatusCode code) => unchecked((int)code);
}
