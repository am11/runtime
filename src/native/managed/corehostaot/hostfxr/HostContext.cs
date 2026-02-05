// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.DotNet.CoreHost;

namespace Microsoft.DotNet.HostFxr;

/// <summary>
/// Represents an initialized host context.
/// </summary>
internal sealed class HostContext : IDisposable
{
    private static int s_nextHandle = 1;
    private static readonly ConcurrentDictionary<nint, HostContext> s_contexts = new();

    private readonly ConcurrentDictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly string? _runtimeConfigPath;
    private readonly string[]? _commandLineArgs;
    private readonly string? _hostPath;
    private readonly string? _dotnetRoot;
    private readonly List<FrameworkReference> _resolvedFrameworks = [];
    private bool _isDisposed;
    private bool _runtimeLoaded;

    public nint Handle { get; }
    public RuntimeConfig? RuntimeConfig { get; private set; }
    public IReadOnlyList<FrameworkReference> ResolvedFrameworks => _resolvedFrameworks;
    public string? DotnetRoot => _dotnetRoot;
    public string? HostPath => _hostPath;
    public string[]? CommandLineArgs => _commandLineArgs;

    public bool IsForCommandLine => _commandLineArgs is not null;

    public bool IsRuntimeLoaded => _runtimeLoaded;

    private HostContext(string? runtimeConfigPath, string[]? commandLineArgs, string? hostPath, string? dotnetRoot)
    {
        _runtimeConfigPath = runtimeConfigPath;
        _commandLineArgs = commandLineArgs;
        _hostPath = hostPath;
        _dotnetRoot = dotnetRoot;
        Handle = Interlocked.Increment(ref s_nextHandle);
        s_contexts[Handle] = this;
    }

    public static HostContext? CreateForRuntimeConfig(string runtimeConfigPath, string? hostPath, string? dotnetRoot)
    {
        var context = new HostContext(runtimeConfigPath, null, hostPath, dotnetRoot);
        if (!context.Initialize())
        {
            context.Dispose();
            return null;
        }

        return context;
    }

    public static HostContext? CreateForCommandLine(string[] args, string? hostPath, string? dotnetRoot)
    {
        var context = new HostContext(null, args, hostPath, dotnetRoot);
        if (!context.InitializeForCommandLine())
        {
            context.Dispose();
            return null;
        }

        return context;
    }

    private bool Initialize()
    {
        if (string.IsNullOrEmpty(_runtimeConfigPath))
        {
            return false;
        }

        RuntimeConfig = RuntimeConfig.Parse(_runtimeConfigPath);
        if (RuntimeConfig is null || !RuntimeConfig.IsValid)
        {
            return false;
        }

        // Copy config properties to context properties
        foreach (var kvp in RuntimeConfig.Properties)
        {
            _properties[kvp.Key] = kvp.Value;
        }

        // Resolve frameworks
        string effectiveDotnetRoot = _dotnetRoot ?? DotNetLocator.GetDefaultDotNetRoot() ?? "";
        if (!string.IsNullOrEmpty(effectiveDotnetRoot))
        {
            var resolver = new FrameworkResolver(effectiveDotnetRoot);
            resolver.ResolveFrameworks(RuntimeConfig, _resolvedFrameworks);
        }

        return true;
    }

    private bool InitializeForCommandLine()
    {
        if (_commandLineArgs is null || _commandLineArgs.Length == 0)
        {
            return false;
        }

        // Find the app to run - first argument should be the app path
        string appPath = _commandLineArgs[0];
        if (!appPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Look for the runtime config
        string configPath = appPath[..^4] + ".runtimeconfig.json";
        RuntimeConfig = RuntimeConfig.Parse(configPath);

        if (RuntimeConfig is not null && RuntimeConfig.IsValid)
        {
            // Copy config properties
            foreach (var kvp in RuntimeConfig.Properties)
            {
                _properties[kvp.Key] = kvp.Value;
            }

            // Resolve frameworks
            string effectiveDotnetRoot = _dotnetRoot ?? DotNetLocator.GetDefaultDotNetRoot() ?? "";
            if (!string.IsNullOrEmpty(effectiveDotnetRoot))
            {
                var resolver = new FrameworkResolver(effectiveDotnetRoot);
                resolver.ResolveFrameworks(RuntimeConfig, _resolvedFrameworks);
            }
        }

        return true;
    }

    public static HostContext? FromHandle(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        return s_contexts.TryGetValue(handle, out HostContext? context) ? context : null;
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

    public void MarkRuntimeLoaded()
    {
        _runtimeLoaded = true;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            s_contexts.TryRemove(Handle, out _);
        }
    }
}
