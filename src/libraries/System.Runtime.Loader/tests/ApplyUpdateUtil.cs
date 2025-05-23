// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;
using Microsoft.DotNet.RemoteExecutor;

namespace System.Reflection.Metadata
{
    public class ApplyUpdateUtil {
        internal const string DotNetModifiableAssembliesSwitch = "DOTNET_MODIFIABLE_ASSEMBLIES";
        internal const string DotNetModifiableAssembliesValue = "debug";

        /// Whether ApplyUpdate is supported by the environment, test configuration, and runtime.
        ///
        /// We need:
        /// 1. Either DOTNET_MODIFIABLE_ASSEMBLIES=debug is set, or we can use the RemoteExecutor to run a child process with that environment; and,
        /// 2. Either Mono in a supported configuration (interpreter as the execution engine, and the hot reload component enabled), or CoreCLR; and,
        /// 3. The test assemblies are compiled with Debug information (this is configured by setting EmitDebugInformation in ApplyUpdate\Directory.Build.props)
        public static bool IsSupported => (IsModifiableAssembliesSet || IsRemoteExecutorSupported) &&
            (!IsMonoRuntime || IsSupportedMonoConfiguration);

        /// true if the current runtime was not launched with the appropriate settings for applying
        /// updates (DOTNET_MODIFIABLE_ASSEMBLIES unset), but we can use the remote executor to
        /// launch a child process that has the right setting.
        public static bool TestUsingRemoteExecutor => IsRemoteExecutorSupported && !IsModifiableAssembliesSet;

        /// true if the current runtime was launched with the appropriate settings for applying
        /// updates (DOTNET_MODIFIABLE_ASSEMBLIES set, and if Mono, the interpreter is enabled).
        public static bool TestUsingLaunchEnvironment => (!IsMonoRuntime || IsSupportedMonoConfiguration) && IsModifiableAssembliesSet;

        public static bool IsModifiableAssembliesSet =>
            String.Equals(DotNetModifiableAssembliesValue, Environment.GetEnvironmentVariable(DotNetModifiableAssembliesSwitch), StringComparison.InvariantCultureIgnoreCase);

        // static cctor for RemoteExecutor throws on wasm.
        public static bool IsRemoteExecutorSupported => !RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER")) && RemoteExecutor.IsSupported;

        public static bool IsMonoRuntime => PlatformDetection.IsMonoRuntime;

        public static bool IsNotMonoRuntime => !PlatformDetection.IsMonoRuntime;

        private static readonly Lazy<bool> s_isSupportedMonoConfiguration = new Lazy<bool>(CheckSupportedMonoConfiguration);

        public static bool IsSupportedMonoConfiguration => s_isSupportedMonoConfiguration.Value;

        // Not every build of Mono supports ApplyUpdate
        internal static bool CheckSupportedMonoConfiguration()
        {
            // check that interpreter is enabled, and the build has hot reload capabilities enabled.
            var isInterp = RuntimeFeature.IsDynamicCodeSupported && !RuntimeFeature.IsDynamicCodeCompiled;
            return isInterp && !PlatformDetection.IsMonoAOT && HasApplyUpdateCapabilities();
        }

        internal static bool HasApplyUpdateCapabilities()
        {
            var ty = typeof(MetadataUpdater);
            var mi = ty.GetMethod("GetCapabilities", BindingFlags.NonPublic | BindingFlags.Static, Array.Empty<Type>());

            if (mi == null)
                return false;

            var caps = mi.Invoke(null, null);

            // any non-empty string, assumed to be at least "baseline"
            return caps is string {Length: > 0};
        }

        private static System.Collections.Generic.Dictionary<Assembly, int> assembly_count = new();

        internal static void ApplyUpdate (System.Reflection.Assembly assm, bool usePDB = true)
        {
            int count;
            if (!assembly_count.TryGetValue(assm, out count))
                count = 1;
            else
                count++;
            assembly_count [assm] = count;

            /* FIXME WASM: Location is empty on wasm. Make up a name based on Name */
            string basename = assm.Location;
            if (basename == "")
                basename = assm.GetName().Name + ".dll";
            Console.Error.WriteLine($"Applying metadata update for {basename}, revision {count}");

            string dmeta_name = $"{basename}.{count}.dmeta";
            string dil_name = $"{basename}.{count}.dil";
            string dpdb_name = $"{basename}.{count}.dpdb";
            byte[] dmeta_data = System.IO.File.ReadAllBytes(dmeta_name);
            byte[] dil_data = System.IO.File.ReadAllBytes(dil_name);
            byte[] dpdb_data = null;

            if (usePDB)
                dpdb_data = System.IO.File.ReadAllBytes(dpdb_name);

            MetadataUpdater.ApplyUpdate(assm, dmeta_data, dil_data, dpdb_data);
        }

        internal static void AddRemoteInvokeOptions (ref RemoteInvokeOptions options)
        {
            options = options ?? new RemoteInvokeOptions();
            options.StartInfo.EnvironmentVariables.Add(DotNetModifiableAssembliesSwitch, DotNetModifiableAssembliesValue);
            /* Ask mono to use .dpdb data to generate sequence points even without a debugger attached */
            if (IsMonoRuntime)
                AppendEnvironmentVariable(options.StartInfo.EnvironmentVariables, "MONO_DEBUG", "gen-seq-points");
        }

        private static void AppendEnvironmentVariable(System.Collections.Specialized.StringDictionary env, string key, string addedValue)
        {
            if (!env.ContainsKey(key))
                env.Add(key, addedValue);
            else
            {
                string oldValue = env[key];
                env[key] = oldValue + "," + addedValue;
            }
        }

        /// Run the given test case, which applies updates to the given assembly.
        ///
        /// Note that the testBody should be a static delegate or a static
        /// lambda - it must not use state from the enclosing method.
        public static void TestCase(Action testBody,
                                    RemoteInvokeOptions options = null)
        {
            if (TestUsingRemoteExecutor)
            {
                Console.Error.WriteLine ($"Running test using RemoteExecutor");
                AddRemoteInvokeOptions(ref options);
                RemoteExecutor.Invoke(testBody, options).Dispose();
            }
            else
            {
                Console.Error.WriteLine($"Running test using direct invoke");
                testBody();
            }
        }

        /// Run the given test case, which applies updates to the given
        /// assembly, and has 1 additional argument.
        ///
        /// Note that the testBody should be a static delegate or a static
        /// lambda - it must not use state from the enclosing method.
        public static void TestCase(Action<string> testBody,
                                    string arg1,
                                    RemoteInvokeOptions options = null)
        {
            if (TestUsingRemoteExecutor)
            {
                AddRemoteInvokeOptions(ref options);
                RemoteExecutor.Invoke(testBody, arg1, options).Dispose();
            }
            else
            {
                testBody(arg1);
            }
        }


        public static void ClearAllReflectionCaches()
        {
            // TODO: Implement for Mono, see https://github.com/dotnet/runtime/issues/50978
            if (IsMonoRuntime)
                return;
            var clearCacheMethod = GetClearCacheMethod();
            clearCacheMethod (null);
        }

        // CoreCLR only
        private static Action<Type[]> GetClearCacheMethod()
        {
            // TODO: Unify with src/libraries/System.Runtime/tests/System/Reflection/ReflectionCacheTests.cs
            Type updateHandler = typeof(Type).Assembly.GetType("System.Reflection.Metadata.RuntimeTypeMetadataUpdateHandler", throwOnError: true, ignoreCase: false);
            MethodInfo clearCache = updateHandler.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(Type[]) });
            Assert.NotNull(clearCache);
            return clearCache.CreateDelegate<Action<Type[]>>();
        }

    }
}
