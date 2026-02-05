// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.HostPolicy;

/// <summary>
/// Represents an initialized hostpolicy context.
/// </summary>
internal sealed class PolicyContext : IDisposable
{
    private static int s_nextHandle = 1;
    private static readonly ConcurrentDictionary<nint, PolicyContext> s_contexts = new();

    private readonly ConcurrentDictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly string? _hostPath;
    private readonly string? _dotnetRoot;
    private readonly List<FrameworkReference> _resolvedFrameworks = [];
    private RuntimeHost? _runtimeHost;
    private bool _isDisposed;
    private bool _runtimeLoaded;

    public nint Handle { get; }

    public bool IsRuntimeLoaded => _runtimeLoaded;
    public string? DotnetRoot => _dotnetRoot;
    public IReadOnlyList<FrameworkReference> ResolvedFrameworks => _resolvedFrameworks;

    private PolicyContext(string? hostPath, string? dotnetRoot)
    {
        _hostPath = hostPath;
        _dotnetRoot = dotnetRoot;
        Handle = Interlocked.Increment(ref s_nextHandle);
        s_contexts[Handle] = this;
    }

    public static PolicyContext Create(string? hostPath, string? dotnetRoot, ReadOnlySpan<(string Key, string Value)> properties)
    {
        var context = new PolicyContext(hostPath, dotnetRoot);
        foreach (var (key, value) in properties)
        {
            context._properties[key] = value;
        }

        return context;
    }

    public void AddResolvedFramework(FrameworkReference framework)
    {
        _resolvedFrameworks.Add(framework);
    }

    public static PolicyContext? FromHandle(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        return s_contexts.TryGetValue(handle, out PolicyContext? context) ? context : null;
    }

    public string? GetProperty(string name)
    {
        return _properties.TryGetValue(name, out string? value) ? value : null;
    }

    public bool SetProperty(string name, string? value)
    {
        if (_runtimeLoaded)
        {
            return false;
        }

        if (value is null)
        {
            _properties.TryRemove(name, out _);
        }
        else
        {
            _properties[name] = value;
        }

        return true;
    }

    public IDictionary<string, string> GetAllPropertiesAsDictionary()
    {
        var dict = new Dictionary<string, string>(_properties.Count, StringComparer.Ordinal);
        foreach (var kvp in _properties)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    public (string[] Keys, string[] Values) GetAllProperties()
    {
        string[] keys = new string[_properties.Count];
        string[] values = new string[_properties.Count];
        int i = 0;
        foreach (var kvp in _properties)
        {
            keys[i] = kvp.Key;
            values[i] = kvp.Value;
            i++;
        }

        return (keys, values);
    }

    /// <summary>
    /// Loads the CoreCLR runtime.
    /// </summary>
    public StatusCode LoadRuntime(string appPath, string? depsFile)
    {
        if (_runtimeLoaded)
        {
            return StatusCode.Success_HostAlreadyInitialized;
        }

        _runtimeHost = RuntimeHost.Create(
            _resolvedFrameworks,
            appPath,
            depsFile,
            GetAllPropertiesAsDictionary());

        if (_runtimeHost is null)
        {
            return StatusCode.CoreClrResolveFailure;
        }

        var result = _runtimeHost.Initialize(appPath);
        if (result == StatusCode.Success)
        {
            _runtimeLoaded = true;
        }

        return result;
    }

    /// <summary>
    /// Executes a managed assembly.
    /// </summary>
    public StatusCode ExecuteAssembly(string assemblyPath, string[] args, out int exitCode)
    {
        exitCode = -1;

        if (_runtimeHost is null)
        {
            return StatusCode.HostInvalidState;
        }

        return _runtimeHost.ExecuteAssembly(assemblyPath, args, out exitCode);
    }

    /// <summary>
    /// Gets a runtime delegate.
    /// </summary>
    public StatusCode GetDelegate(string assemblyName, string typeName, string methodName, out nint delegatePtr)
    {
        delegatePtr = 0;

        if (_runtimeHost is null)
        {
            return StatusCode.HostInvalidState;
        }

        return _runtimeHost.GetDelegate(assemblyName, typeName, methodName, out delegatePtr);
    }

    public void MarkRuntimeLoaded()
    {
        _runtimeLoaded = true;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _runtimeHost?.Dispose();
            s_contexts.TryRemove(Handle, out _);
        }
    }
}
