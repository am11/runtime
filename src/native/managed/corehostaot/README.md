# CoreHost AOT

This directory contains C# implementations of the native host components that are published as NativeAOT libraries.

## Components

The goal is to provide C#-based implementations of these components that can be compiled with NativeAOT to produce native libraries with the same API surface as the original C++ implementations:

- **nethost** - Library for locating the hostfxr library (`get_hostfxr_path`)
- **hostfxr** - Host framework resolver (initialization, property management, runtime delegates)
- **hostpolicy** - Host policy library (runtime loading, dependency resolution)
- **apphost** - Application host executable

## Building

These components are built as part of the `tools.cdac` subset (shared with the cDAC components) via the `compile-native.proj` in the parent directory.

```bash
./build.sh tools.cdac
```

Or build individual projects:

```bash
./dotnet.sh build src/native/managed/corehostaot/nethost/nethost.csproj
./dotnet.sh build src/native/managed/corehostaot/hostfxr/hostfxr.csproj
./dotnet.sh build src/native/managed/corehostaot/hostpolicy/hostpolicy.csproj
```

## Status

This is an experimental implementation.

| Component | Status |
|-----------|--------|
| nethost | ✅ `get_hostfxr_path` API |
| hostfxr | ✅ Initialization, property management, environment enumeration, runtime delegates |
| hostpolicy | ✅ Initialization, property management, runtime loading, `corehost_main_with_output_buffer` |
| apphost | ✅ Basic implementation (can load and run apps) |

### Implemented Features

**Runtime Config Parsing:**
- Parse `.runtimeconfig.json` files
- Framework references (single and multiple)
- Roll forward options
- Config properties

**Framework Resolution:**
- Enumerate installed frameworks and SDKs
- Resolve frameworks based on roll forward policy
- Version comparison with pre-release support

**Host Context Management:**
- Property get/set
- Framework resolution results

**CoreCLR Loading:**
- Load coreclr native library
- Initialize the runtime with properties
- Execute managed assemblies
- Create delegates to managed methods

**Runtime Delegates:**
- `load_assembly_and_get_function_pointer`
- `get_function_pointer`
- `load_assembly` (stub)
- `load_assembly_bytes` (stub)

**deps.json Parsing:**
- Parse runtime target and libraries
- Extract assembly and native asset paths
- RID fallback graph support
- Build TPA list from deps.json entries

**Multi-Level Lookup:**
- DOTNET_MULTILEVEL_LOOKUP environment variable support
- Global .NET installation discovery (Windows registry, Unix config files)
- Platform-specific default locations
- Framework resolution across multiple locations

**Single-File Bundles:**
- Bundle marker detection and header offset parsing
- Bundle header parsing (v1-v6 format support)
- Manifest and file entry parsing
- Memory-mapped file reading
- Compressed file support (deflate)
- Native binary extraction
- macOS universal binary (FAT) support
- Runtime config and deps.json reading from bundle
- DOTNET_BUNDLE_EXTRACT_BASE_DIR environment variable support

### Not Yet Implemented

- Runtime stores
- Additional probing paths

## Architecture

```
common/
├── StatusCode.cs          # Shared error codes matching error_codes.h
├── DotNetLocator.cs       # .NET installation discovery logic
├── RollForwardOption.cs   # Roll forward policy enum
├── FrameworkVersion.cs    # Semantic version parsing
├── FrameworkReference.cs  # Framework reference model
├── FrameworkResolver.cs   # Framework resolution logic (with multi-level lookup)
├── RuntimeConfig.cs       # .runtimeconfig.json parser
├── DepsJson.cs            # .deps.json parser
├── MultiLevelLookup.cs    # Multi-level lookup implementation
├── CoreClrLoader.cs       # CoreCLR native library loader
├── RuntimePropertyBag.cs  # Runtime property management
├── RuntimeDelegates.cs    # Runtime delegate implementations
├── RuntimeHost.cs         # Runtime lifecycle management
├── HostFxrDelegateType.cs # Delegate type enumeration
└── Bundle/
    ├── BundleFileType.cs  # Bundle file type enumeration
    ├── BundleFileEntry.cs # Bundle file entry model
    ├── BundleHeader.cs    # Bundle header parsing
    ├── BundleManifest.cs  # Bundle manifest parsing
    ├── BundleReader.cs    # Memory-mapped bundle reader
    ├── BundleExtractor.cs # Bundle file extraction
    └── BundleProbe.cs     # Bundle probing integration

nethost/
├── nethost.csproj
└── NetHostExports.cs      # get_hostfxr_path export

hostfxr/
├── hostfxr.csproj
├── HostFxrTypes.cs        # Native struct definitions
├── HostContext.cs         # Host context management
└── HostFxrExports.cs      # Native exports

hostpolicy/
├── hostpolicy.csproj
├── HostPolicyTypes.cs     # Native struct definitions
├── PolicyContext.cs       # Policy context management
└── HostPolicyExports.cs   # Native exports

apphost/
├── apphost.csproj
└── Program.cs             # Application host entry point (with bundle support)
```
