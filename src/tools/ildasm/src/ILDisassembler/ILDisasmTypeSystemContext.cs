// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILDisassembler
{
    /// <summary>
    /// Resolver interface for loading referenced assemblies.
    /// </summary>
    public interface IAssemblyResolver
    {
        PEReader ResolveAssembly(AssemblyNameInfo assemblyName);
    }

    /// <summary>
    /// Simple assembly resolver that looks for assemblies in multiple directories.
    /// </summary>
    public sealed class SimpleAssemblyResolver : IAssemblyResolver
    {
        private readonly List<string> _searchDirectories = new List<string>();

        public SimpleAssemblyResolver(string searchDirectory)
        {
            if (!string.IsNullOrEmpty(searchDirectory))
            {
                _searchDirectories.Add(searchDirectory);
            }

            // Add runtime directory to resolve System.Runtime etc.
            string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (!string.IsNullOrEmpty(runtimeDir))
            {
                _searchDirectories.Add(runtimeDir);
            }
        }

        public PEReader ResolveAssembly(AssemblyNameInfo assemblyName)
        {
            foreach (string dir in _searchDirectories)
            {
                string path = Path.Combine(dir, assemblyName.Name + ".dll");
                if (File.Exists(path))
                {
                    return new PEReader(File.OpenRead(path));
                }
            }

            return null;
        }
    }

    /// <summary>
    /// TypeSystemContext for IL disassembly.
    /// </summary>
    internal sealed class ILDisasmTypeSystemContext : MetadataTypeSystemContext
    {
        private readonly IAssemblyResolver _resolver;
        private readonly Dictionary<string, EcmaModule> _moduleCache = new Dictionary<string, EcmaModule>(StringComparer.OrdinalIgnoreCase);

        public ILDisasmTypeSystemContext(IAssemblyResolver resolver)
        {
            _resolver = resolver;
        }

        public override ModuleDesc ResolveAssembly(AssemblyNameInfo name, bool throwIfNotFound = true)
        {
            if (_moduleCache.TryGetValue(name.Name, out var module))
            {
                return module;
            }

            PEReader peReader = _resolver.ResolveAssembly(name);
            if (peReader != null)
            {
                module = EcmaModule.Create(this, peReader, null);
                _moduleCache[name.Name] = module;

                // Set System.Private.CoreLib as the system module for WellKnownType resolution
                if (SystemModule == null && name.Name == "System.Private.CoreLib")
                {
                    SetSystemModule(module);
                }

                return module;
            }

            // For disassembly, we don't want to throw when assemblies can't be resolved
            // We'll just output what we can without full type resolution
            return null;
        }

        public EcmaModule GetModuleForSimpleName(string simpleName, PEReader peReader)
        {
            if (_moduleCache.TryGetValue(simpleName, out var module))
            {
                return module;
            }

            module = EcmaModule.Create(this, peReader, null);
            _moduleCache[simpleName] = module;
            return module;
        }

        /// <summary>
        /// Try to initialize the system module by loading System.Private.CoreLib.
        /// </summary>
        public void EnsureSystemModule()
        {
            if (SystemModule != null)
            {
                return;
            }

            // Try to resolve System.Private.CoreLib
            var coreLibName = new AssemblyNameInfo("System.Private.CoreLib");
            ResolveAssembly(coreLibName, throwIfNotFound: false);
        }
    }
}
