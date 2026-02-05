// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.CoreHost;

/// <summary>
/// Provides functionality to load and interact with the CoreCLR runtime.
/// </summary>
internal sealed unsafe class CoreClrLoader : IDisposable
{
    private nint _libraryHandle;
    private nint _hostHandle;
    private uint _domainId;
    private bool _isInitialized;
    private bool _isShutdown;

    // CoreCLR function pointers
    private delegate* unmanaged[Stdcall]<byte*, byte*, int, byte**, byte**, nint*, uint*, int> _coreclrInitialize;
    private delegate* unmanaged[Stdcall]<nint, uint, int*, int> _coreclrShutdown;
    private delegate* unmanaged[Stdcall]<nint, uint, int, byte**, byte*, uint*, int> _coreclrExecuteAssembly;
    private delegate* unmanaged[Stdcall]<nint, uint, byte*, byte*, byte*, void**, int> _coreclrCreateDelegate;
    private delegate* unmanaged[Stdcall]<delegate* unmanaged<byte*, void>, int> _coreclrSetErrorWriter;

    private CoreClrLoader(nint libraryHandle)
    {
        _libraryHandle = libraryHandle;
    }

    /// <summary>
    /// Loads the CoreCLR library from the specified path.
    /// </summary>
    public static CoreClrLoader? Load(string coreclrPath)
    {
        if (!File.Exists(coreclrPath))
        {
            return null;
        }

        nint handle = NativeLibrary.Load(coreclrPath);
        if (handle == 0)
        {
            return null;
        }

        var loader = new CoreClrLoader(handle);
        if (!loader.BindFunctions())
        {
            NativeLibrary.Free(handle);
            return null;
        }

        return loader;
    }

    private bool BindFunctions()
    {
        _coreclrInitialize = (delegate* unmanaged[Stdcall]<byte*, byte*, int, byte**, byte**, nint*, uint*, int>)
            GetExport("coreclr_initialize");
        _coreclrShutdown = (delegate* unmanaged[Stdcall]<nint, uint, int*, int>)
            GetExport("coreclr_shutdown");
        _coreclrExecuteAssembly = (delegate* unmanaged[Stdcall]<nint, uint, int, byte**, byte*, uint*, int>)
            GetExport("coreclr_execute_assembly");
        _coreclrCreateDelegate = (delegate* unmanaged[Stdcall]<nint, uint, byte*, byte*, byte*, void**, int>)
            GetExport("coreclr_create_delegate");

        // Optional function
        _coreclrSetErrorWriter = (delegate* unmanaged[Stdcall]<delegate* unmanaged<byte*, void>, int>)
            GetExportOptional("coreclr_set_error_writer");

        return _coreclrInitialize is not null
            && _coreclrShutdown is not null
            && _coreclrExecuteAssembly is not null
            && _coreclrCreateDelegate is not null;
    }

    private nint GetExport(string name)
    {
        return NativeLibrary.GetExport(_libraryHandle, name);
    }

    private nint GetExportOptional(string name)
    {
        NativeLibrary.TryGetExport(_libraryHandle, name, out nint address);
        return address;
    }

    /// <summary>
    /// Initializes the CoreCLR runtime with the specified properties.
    /// </summary>
    public StatusCode Initialize(
        string exePath,
        string appDomainFriendlyName,
        string[] propertyKeys,
        string[] propertyValues)
    {
        if (_isInitialized)
        {
            return StatusCode.HostInvalidState;
        }

        if (propertyKeys.Length != propertyValues.Length)
        {
            return StatusCode.InvalidArgFailure;
        }

        // Convert strings to UTF-8
        byte[] exePathBytes = ToUtf8NullTerminated(exePath);
        byte[] appDomainNameBytes = ToUtf8NullTerminated(appDomainFriendlyName);

        int propertyCount = propertyKeys.Length;
        byte[][] keyBytes = new byte[propertyCount][];
        byte[][] valueBytes = new byte[propertyCount][];

        for (int i = 0; i < propertyCount; i++)
        {
            keyBytes[i] = ToUtf8NullTerminated(propertyKeys[i]);
            valueBytes[i] = ToUtf8NullTerminated(propertyValues[i]);
        }

        nint hostHandle;
        uint domainId;
        int hr;

        fixed (byte* exePathPtr = exePathBytes)
        fixed (byte* appDomainNamePtr = appDomainNameBytes)
        {
            // Create arrays of pointers
            byte*[] keyPtrs = new byte*[propertyCount];
            byte*[] valuePtrs = new byte*[propertyCount];
            GCHandle[] keyHandles = new GCHandle[propertyCount];
            GCHandle[] valueHandles = new GCHandle[propertyCount];

            try
            {
                for (int i = 0; i < propertyCount; i++)
                {
                    keyHandles[i] = GCHandle.Alloc(keyBytes[i], GCHandleType.Pinned);
                    valueHandles[i] = GCHandle.Alloc(valueBytes[i], GCHandleType.Pinned);
                    keyPtrs[i] = (byte*)keyHandles[i].AddrOfPinnedObject();
                    valuePtrs[i] = (byte*)valueHandles[i].AddrOfPinnedObject();
                }

                fixed (byte** keysPtr = keyPtrs)
                fixed (byte** valuesPtr = valuePtrs)
                {
                    hr = _coreclrInitialize(
                        exePathPtr,
                        appDomainNamePtr,
                        propertyCount,
                        keysPtr,
                        valuesPtr,
                        &hostHandle,
                        &domainId);
                }
            }
            finally
            {
                for (int i = 0; i < propertyCount; i++)
                {
                    if (keyHandles[i].IsAllocated) keyHandles[i].Free();
                    if (valueHandles[i].IsAllocated) valueHandles[i].Free();
                }
            }
        }

        if (hr < 0)
        {
            return StatusCode.CoreClrInitFailure;
        }

        _hostHandle = hostHandle;
        _domainId = domainId;
        _isInitialized = true;

        return StatusCode.Success;
    }

    /// <summary>
    /// Executes a managed assembly.
    /// </summary>
    public StatusCode ExecuteAssembly(string assemblyPath, string[] args, out int exitCode)
    {
        exitCode = -1;

        if (!_isInitialized || _isShutdown)
        {
            return StatusCode.HostInvalidState;
        }

        byte[] assemblyPathBytes = ToUtf8NullTerminated(assemblyPath);
        byte[][] argBytes = new byte[args.Length][];
        for (int i = 0; i < args.Length; i++)
        {
            argBytes[i] = ToUtf8NullTerminated(args[i]);
        }

        uint exitCodeUint;
        int hr;

        fixed (byte* assemblyPathPtr = assemblyPathBytes)
        {
            byte*[] argPtrs = new byte*[args.Length];
            GCHandle[] argHandles = new GCHandle[args.Length];

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    argHandles[i] = GCHandle.Alloc(argBytes[i], GCHandleType.Pinned);
                    argPtrs[i] = (byte*)argHandles[i].AddrOfPinnedObject();
                }

                fixed (byte** argsPtr = argPtrs)
                {
                    hr = _coreclrExecuteAssembly(
                        _hostHandle,
                        _domainId,
                        args.Length,
                        argsPtr,
                        assemblyPathPtr,
                        &exitCodeUint);
                }
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (argHandles[i].IsAllocated) argHandles[i].Free();
                }
            }
        }

        exitCode = (int)exitCodeUint;

        if (hr < 0)
        {
            return StatusCode.CoreClrExeFailure;
        }

        return StatusCode.Success;
    }

    /// <summary>
    /// Creates a delegate to a managed method.
    /// </summary>
    public StatusCode CreateDelegate(
        string assemblyName,
        string typeName,
        string methodName,
        out nint delegatePtr)
    {
        delegatePtr = 0;

        if (!_isInitialized || _isShutdown)
        {
            return StatusCode.HostInvalidState;
        }

        byte[] assemblyNameBytes = ToUtf8NullTerminated(assemblyName);
        byte[] typeNameBytes = ToUtf8NullTerminated(typeName);
        byte[] methodNameBytes = ToUtf8NullTerminated(methodName);

        void* del;
        int hr;

        fixed (byte* assemblyNamePtr = assemblyNameBytes)
        fixed (byte* typeNamePtr = typeNameBytes)
        fixed (byte* methodNamePtr = methodNameBytes)
        {
            hr = _coreclrCreateDelegate(
                _hostHandle,
                _domainId,
                assemblyNamePtr,
                typeNamePtr,
                methodNamePtr,
                &del);
        }

        if (hr < 0)
        {
            return StatusCode.HostApiFailed;
        }

        delegatePtr = (nint)del;

        return StatusCode.Success;
    }

    /// <summary>
    /// Shuts down the CoreCLR runtime.
    /// </summary>
    public StatusCode Shutdown(out int latchedExitCode)
    {
        latchedExitCode = 0;

        if (!_isInitialized)
        {
            return StatusCode.HostInvalidState;
        }

        if (_isShutdown)
        {
            return StatusCode.Success;
        }

        int exitCode;
        int hr = _coreclrShutdown(_hostHandle, _domainId, &exitCode);
        latchedExitCode = exitCode;
        _isShutdown = true;

        if (hr < 0)
        {
            return StatusCode.HostApiFailed;
        }

        return StatusCode.Success;
    }

    private static byte[] ToUtf8NullTerminated(string str)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(str);
        byte[] bytes = new byte[byteCount + 1];
        System.Text.Encoding.UTF8.GetBytes(str, bytes);
        bytes[byteCount] = 0;

        return bytes;
    }

    public void Dispose()
    {
        if (_isInitialized && !_isShutdown)
        {
            Shutdown(out _);
        }

        if (_libraryHandle != 0)
        {
            NativeLibrary.Free(_libraryHandle);
            _libraryHandle = 0;
        }
    }
}

/// <summary>
/// Common runtime properties used when initializing CoreCLR.
/// </summary>
internal static class RuntimePropertyNames
{
    public const string TrustedPlatformAssemblies = "TRUSTED_PLATFORM_ASSEMBLIES";
    public const string NativeDllSearchDirectories = "NATIVE_DLL_SEARCH_DIRECTORIES";
    public const string PlatformResourceRoots = "PLATFORM_RESOURCE_ROOTS";
    public const string AppContextBaseDirectory = "APP_CONTEXT_BASE_DIRECTORY";
    public const string AppContextDepsFiles = "APP_CONTEXT_DEPS_FILES";
    public const string FxDepsFile = "FX_DEPS_FILE";
    public const string ProbingDirectories = "PROBING_DIRECTORIES";
    public const string StartUpHooks = "STARTUP_HOOKS";
    public const string AppPaths = "APP_PATHS";
    public const string RuntimeIdentifier = "RUNTIME_IDENTIFIER";
}
