// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "deps_entry.h"
#include "deps_format.h"
#include "utils.h"
#include "trace.h"
#include "bundle/info.h"
#include <tuple>
#include <array>
#include <iterator>
#include <cassert>
#include <functional>
#include <minipal/utils.h>

const std::array<const pal::char_t*, deps_entry_t::asset_types::count> deps_entry_t::s_known_asset_types = {{
    _X("runtime"), _X("resources"), _X("native")
}};

namespace
{
    pal::string_t get_optional_property(
        const simdjson::dom::element& properties,
        const pal::string_t& key)
    {
        auto prop = properties[key.c_str()];
        return (!prop.error() && !prop.get_string().error()) ? std::string(prop.get_string().value()) : _X("");
    }

    pal::string_t get_optional_path(
        const simdjson::dom::element& properties,
        const pal::string_t& key)
    {
        pal::string_t path = get_optional_property(properties, key);

        if (path.length() > 0 && _X('/') != DIR_SEPARATOR)
        {
            replace_char(&path, _X('/'), DIR_SEPARATOR);
        }

        return path;
    }

    void populate_rid_fallback_graph(const simdjson::dom::element& json, deps_json_t::rid_fallback_graph_t& rid_fallback_graph)
    {
        if (!json.is_object())
            return;

        auto runtimes_elem = json.at_pointer(_X("/runtimes"));
        if (runtimes_elem.error() || !runtimes_elem.is_object())
            return;

        if (runtimes_elem.begin() != runtimes_elem.end())
        {
            auto runtimes_obj = runtimes_elem.get_object();
            if (!runtimes_obj.error())
            {
                for (auto rid : runtimes_obj)
                {
                    auto& vec = rid_fallback_graph[std::string(rid.key)];
                    simdjson::dom::array fallback_array;
                    if (rid.value.get_array().get(fallback_array) == simdjson::SUCCESS)
                    {
                        vec.reserve(fallback_array.size());
                        for (auto fallback : fallback_array)
                        {
                            auto fallback_str = fallback.get_string();
                            if (!fallback_str.error())
                            {
                                vec.push_back(std::string(fallback_str.value()));
                            }
                        }
                    }
                }
            }
        }

        if (trace::is_enabled())
        {
            trace::verbose(_X("RID fallback graph = {"));
            for (const auto& rid : rid_fallback_graph)
            {
                trace::verbose(_X("%s => ["), rid.first.c_str());
                for (const auto& fallback : rid.second)
                {
                    trace::verbose(_X("%s, "), fallback.c_str());
                }
                trace::verbose(_X("]"));
            }
            trace::verbose(_X("}"));
        }
    }

    bool deps_file_exists(pal::string_t& deps_path)
    {
        if (bundle::info_t::config_t::probe(deps_path) || pal::fullpath(&deps_path, /*skip_error_logging*/ true))
            return true;

        trace::verbose(_X("Dependencies manifest does not exist at [%s]"), deps_path.c_str());
        return false;
    }
}

deps_json_t::rid_fallback_graph_t deps_json_t::get_rid_fallback_graph(const pal::string_t& deps_path)
{
    rid_fallback_graph_t rid_fallback_graph;
    trace::verbose(_X("Getting RID fallback graph for deps file... %s"), deps_path.c_str());

    pal::string_t deps_path_local = deps_path;
    if (!deps_file_exists(deps_path_local))
        return rid_fallback_graph;

    json_parser_t json;
    if (!json.parse_file(deps_path_local))
        return rid_fallback_graph;

    populate_rid_fallback_graph(json.document(), rid_fallback_graph);
    return rid_fallback_graph;
}

void deps_json_t::reconcile_libraries_with_targets(
    const simdjson::dom::element& json,
    const std::function<bool(const pal::string_t&)>& library_has_assets_fn,
    const std::function<const vec_asset_t&(const pal::string_t&, size_t, bool*)>& get_assets_fn)
{
    pal::string_t deps_file = get_filename(m_deps_file);

    simdjson::dom::object libraries;
    if (json[_X("libraries")].get_object().get(libraries) != simdjson::SUCCESS)
        return;

    for (auto library : libraries)
    {
        trace::info(_X("Reconciling library %s"), library.key.data());

        pal::string_t lib_name{library.key.data()};
        if (!library_has_assets_fn(lib_name))
        {
            trace::info(_X("  No assets for library %s"), library.key.data());
            continue;
        }

        pal::string_t hash = get_optional_property(library.value, _X("sha512"));
        bool serviceable = library.value[_X("serviceable")].get_bool().value();

        pal::string_t library_path = get_optional_path(library.value, _X("path"));
        pal::string_t library_hash_path = get_optional_path(library.value, _X("hashPath"));
        pal::string_t runtime_store_manifest_list = get_optional_path(library.value, _X("runtimeStoreManifestName"));
        pal::string_t library_type = to_lower(library.value[_X("type")].get_string().value().data());

        size_t pos = lib_name.find(_X("/"));
        pal::string_t library_name = lib_name.substr(0, pos);
        pal::string_t library_version = lib_name.substr(pos + 1);

        trace::info(_X("  %s: %s, version: %s"), library_type.c_str(), library_name.c_str(), library_version.c_str());

        for (size_t i = 0; i < deps_entry_t::s_known_asset_types.size(); ++i)
        {
            bool rid_specific = false;
            const vec_asset_t& assets = get_assets_fn(lib_name, i, &rid_specific);
            if (assets.empty())
                continue;

            trace::info(_X("  Adding %s assets"), deps_entry_t::s_known_asset_types[i]);
            m_deps_entries[i].reserve(assets.size());

            for (const auto& asset : assets)
            {
                auto asset_name = asset.name;
                if (utils::ends_with(asset_name, _X(".ni"), false))
                {
                    asset_name = strip_file_ext(asset_name);
                }

                deps_entry_t entry;
                entry.library_name = library_name;
                entry.library_version = library_version;
                entry.library_type = library_type;
                entry.library_hash = hash;
                entry.library_path = library_path;
                entry.library_hash_path = library_hash_path;
                entry.runtime_store_manifest_list = runtime_store_manifest_list;
                entry.asset_type = static_cast<deps_entry_t::asset_types>(i);
                entry.is_serviceable = serviceable;
                entry.is_rid_specific = rid_specific;
                entry.deps_file = deps_file;
                entry.asset = asset;
                entry.asset.name = asset_name;

                if (trace::is_enabled())
                {
                    trace::info(_X("    Entry %zu for asset name: %s, relpath: %s, assemblyVersion %s, fileVersion %s"),
                        m_deps_entries[i].size(),
                        entry.asset.name.c_str(),
                        entry.asset.relative_path.c_str(),
                        entry.asset.assembly_version.as_str().c_str(),
                        entry.asset.file_version.as_str().c_str());
                }

                m_deps_entries[i].push_back(std::move(entry));
            }
        }
    }
}

namespace
{
    #define CURRENT_ARCH_SUFFIX _X("-") _STRINGIFY(CURRENT_ARCH_NAME)
    #define RID_CURRENT_ARCH_LIST(os) \
        _X(os) CURRENT_ARCH_SUFFIX, \
        _X(os),

    const pal::char_t* s_host_rids[] =
    {
#if defined(TARGET_WINDOWS)
        RID_CURRENT_ARCH_LIST(HOST_RID_PLATFORM)
#elif defined(TARGET_OSX)
        RID_CURRENT_ARCH_LIST(HOST_RID_PLATFORM)
        RID_CURRENT_ARCH_LIST("unix")
#elif defined(TARGET_ANDROID)
        RID_CURRENT_ARCH_LIST(HOST_RID_PLATFORM)
        RID_CURRENT_ARCH_LIST("linux")
        RID_CURRENT_ARCH_LIST("unix")
#else
        // Covers non-portable RIDs
        RID_CURRENT_ARCH_LIST(HOST_RID_PLATFORM)
#if defined(TARGET_LINUX_MUSL)
        RID_CURRENT_ARCH_LIST("linux-musl")
        RID_CURRENT_ARCH_LIST("linux")
#elif !defined(FALLBACK_OS_IS_SAME_AS_TARGET_OS)
        // Covers "linux" and non-linux like "freebsd", "illumos"
        RID_CURRENT_ARCH_LIST(CURRENT_OS_NAME)
#endif
        RID_CURRENT_ARCH_LIST("unix")
#endif
        _X("any"),
    };

    // Returns the RID determined (computed or fallback) for the machine the host is running on.
    // This RID is discoved at run-time from OS APIs and/or files. It may be distro-specific and/or
    // version-specific. This usage of the machine RID is for a backwards-compat option that relies
    // on the computed RID. All other parts of the host use the compile-time RID corresponding to the
    // platform for which the runtime was built.
    pal::string_t get_current_machine_rid(const deps_json_t::rid_fallback_graph_t* rid_fallback_graph)
    {
        pal::string_t current_rid;
        if (!try_get_runtime_id_from_env(current_rid))
        {
            current_rid = pal::get_current_os_rid_platform();
            if (!current_rid.empty())
            {
                current_rid.append(_X("-"));
                current_rid.append(get_current_arch_name());
            }
        }

        trace::info(_X("HostRID is %s"), current_rid.empty() ? _X("not available") : current_rid.c_str());

        // If the current RID is not present in the RID fallback graph, then the platform
        // is unknown to us. At this point, we will fallback to using the base RIDs and attempt
        // asset lookup using them.
        //
        // We do the same even when the RID is empty.
        if (current_rid.empty() || (rid_fallback_graph != nullptr && rid_fallback_graph->count(current_rid) == 0))
        {
            current_rid = pal::get_current_os_fallback_rid() + pal::string_t(_X("-")) + get_current_arch_name();

            trace::info(_X("Falling back to base HostRID: %s"), current_rid.c_str());
        }

        return current_rid;
    }

    void print_host_rid_list()
    {
        if (trace::is_enabled())
        {
            trace::verbose(_X("Host RID list = ["));
            pal::string_t env_rid;
            if (try_get_runtime_id_from_env(env_rid))
                trace::verbose(_X("  %s,"), env_rid.c_str());

            for (const pal::char_t* rid : s_host_rids)
            {
                trace::verbose(_X("  %s,"), rid);
            }
            trace::verbose(_X("]"));
        }
    }

    bool try_get_matching_rid(const std::unordered_map<pal::string_t, std::vector<deps_asset_t>>& rid_assets, pal::string_t& out_rid)
    {
        // Check for match with environment variable RID value
        pal::string_t env_rid;
        if (try_get_runtime_id_from_env(env_rid))
        {
            if (rid_assets.count(env_rid) != 0)
            {
                out_rid = env_rid;
                return true;
            }
        }

        // Use our list of known portable RIDs
        for (const pal::char_t* rid : s_host_rids)
        {
            const auto& iter = std::find_if(rid_assets.cbegin(), rid_assets.cend(),
                [&](const std::pair<pal::string_t, std::vector<deps_asset_t>>& rid_asset)
                {
                    return pal::strcmp(rid_asset.first.c_str(), rid) == 0;
                });
            if (iter != rid_assets.cend())
            {
                out_rid = rid;
                return true;
            }
        }

        return false;
    }

    bool try_get_matching_rid_with_fallback_graph(const std::unordered_map<pal::string_t, std::vector<deps_asset_t>>& rid_assets, const pal::string_t& host_rid, const deps_json_t::rid_fallback_graph_t& rid_fallback_graph, pal::string_t& out_rid)
    {
        // Check for exact match with the host RID
        if (rid_assets.count(host_rid) != 0)
        {
            out_rid = host_rid;
            return true;
        }

        // Check if the RID exists in the fallback graph
        auto rid_fallback_iter = rid_fallback_graph.find(host_rid);
        if (rid_fallback_iter == rid_fallback_graph.end())
        {
            trace::warning(_X("The targeted framework does not support the runtime '%s'. Some libraries may fail to load on this platform."), host_rid.c_str());
            return false;
        }

        // Find the first RID fallback that has assets
        const auto& fallback_rids = rid_fallback_iter->second;
        auto iter = std::find_if(fallback_rids.begin(), fallback_rids.end(), [&rid_assets](const pal::string_t& rid) {
            return rid_assets.count(rid);
            });
        if (iter != fallback_rids.end())
        {
            out_rid.assign(*iter);
            return true;
        }

        return false;
    }
}

void deps_json_t::perform_rid_fallback(rid_specific_assets_t* portable_assets)
{
    assert(!m_rid_resolution_options.use_fallback_graph || m_rid_resolution_options.rid_fallback_graph != nullptr);

    pal::string_t host_rid;
    if (m_rid_resolution_options.use_fallback_graph)
    {
        host_rid = get_current_machine_rid(m_rid_resolution_options.rid_fallback_graph);
    }
    else
    {
        print_host_rid_list();
    }

    for (auto& package : portable_assets->libs)
    {
        trace::verbose(_X("Filtering RID assets for %s"), package.first.c_str());
        for (size_t asset_type_index = 0; asset_type_index < deps_entry_t::asset_types::count; asset_type_index++)
        {
            auto& rid_assets = package.second[asset_type_index].rid_assets;
            if (rid_assets.empty())
                continue;

            pal::string_t matched_rid;
            bool found_match = m_rid_resolution_options.use_fallback_graph
                ? try_get_matching_rid_with_fallback_graph(rid_assets, host_rid, *m_rid_resolution_options.rid_fallback_graph, matched_rid)
                : try_get_matching_rid(rid_assets, matched_rid);
            if (!found_match)
            {
                trace::verbose(_X("  No matching %s assets for package %s"), deps_entry_t::s_known_asset_types[asset_type_index], package.first.c_str());
                rid_assets.clear();
                continue;
            }

            trace::verbose(_X("  Matched RID %s for %s assets"), matched_rid.c_str(), deps_entry_t::s_known_asset_types[asset_type_index]);
            for (auto iter = rid_assets.begin(); iter != rid_assets.end(); /* */)
            {
                if (iter->first != matched_rid)
                {
                    trace::verbose(_X("    Removing %s assets"),iter->first.c_str(), package.first.c_str());
                    iter = rid_assets.erase(iter);
                }
                else
                {
                    ++iter;
                }
            }
        }
    }
}

void deps_json_t::process_runtime_targets(const simdjson::dom::element& json, const pal::string_t& target_name, rid_specific_assets_t* p_assets)
{
    rid_specific_assets_t& assets = *p_assets;

    auto targets = json[_X("targets")].get_object();
    if (targets.error())
        return;

    auto target_it = targets[target_name].get_object();
    if (target_it.error() || target_it.begin() == target_it.end())
        return;

    simdjson::dom::object target_packages = target_it.value();
    for (auto package : target_packages)
    {
        auto package_obj = package.value.get_object();
        if (package_obj.error())
            continue;

        auto runtime_targets_it = package_obj[_X("runtimeTargets")];
        if (runtime_targets_it.error() || package_obj.begin() == package_obj.end())
            continue;

        trace::info(_X("Processing runtimeTargets for package %s"), package.key.data());

        auto runtime_targets = runtime_targets_it.value().get_object();
        for (auto file : runtime_targets)
        {
            auto file_obj = file.value.get_object();
            if (file_obj.error())
                continue;

            auto type_it = file_obj[_X("assetType")];
            if (type_it.error() || type_it.begin() == type_it.end() || !type_it.is_string())
                continue;

            const auto& type = type_it.get_string().value().data();

            for (size_t asset_type_index = 0; asset_type_index < deps_entry_t::s_known_asset_types.size(); ++asset_type_index)
            {
                if (pal::strcasecmp(type, deps_entry_t::s_known_asset_types[asset_type_index]) != 0)
                    continue;

                version_t assembly_version, file_version;

                const pal::string_t assembly_version_str = get_optional_property(file.value, _X("assemblyVersion"));
                if (!assembly_version_str.empty())
                {
                    version_t::parse(assembly_version_str, &assembly_version);
                }

                const pal::string_t file_version_str = get_optional_property(file.value, _X("fileVersion"));
                if (!file_version_str.empty())
                {
                    version_t::parse(file_version_str, &file_version);
                }

                pal::string_t file_name{file.key.data()};
                deps_asset_t asset(get_filename_without_ext(file_name), file_name, assembly_version, file_version);

                auto rid_it = file_obj[_X("rid")];
                if (rid_it.error() || rid_it.begin() == rid_it.end() || !rid_it.is_string())
                    continue;

                const auto& rid = rid_it.get_string().value();

                if (trace::is_enabled())
                {
                    trace::info(_X("  %s asset: %s rid=%s assemblyVersion=%s fileVersion=%s"),
                        deps_entry_t::s_known_asset_types[asset_type_index],
                        asset.relative_path.c_str(),
                        rid.data(),
                        asset.assembly_version.as_str().c_str(),
                        asset.file_version.as_str().c_str());
                }

                assets.libs[package.key.data()][asset_type_index].rid_assets[rid.data()].push_back(asset);
            }
        }
    }

    perform_rid_fallback(&assets);
}

void deps_json_t::process_targets(const simdjson::dom::element& json, const pal::string_t& target_name, deps_assets_t* p_assets)
{
    deps_assets_t& assets = *p_assets;

    auto targets = json[_X("targets")].get_object();
    if (targets.error())
        return;

    auto target_it = targets[target_name];
    if (target_it.error() || target_it.begin() == target_it.end() || !target_it.is_object())
        return;

    auto packages = target_it.get_object();
    for (auto package : packages)
    {
        trace::info(_X("Processing package %s"), package.key.data());

        auto asset_types = package.value.get_object();
        if (asset_types.error())
            continue;

        for (size_t i = 0; i < deps_entry_t::s_known_asset_types.size(); ++i)
        {
            auto iter = asset_types[deps_entry_t::s_known_asset_types[i]];
            if (iter.begin() == iter.end() || !iter.is_object())
                continue;

            trace::info(_X("  Adding %s assets"), deps_entry_t::s_known_asset_types[i]);

            auto files = iter.get_object();
            vec_asset_t& asset_files = assets.libs[package.key.data()][i];
            asset_files.reserve(files.size());

            for (auto file : files)
            {
                version_t assembly_version, file_version;

                const pal::string_t assembly_version_str = get_optional_property(file.value, _X("assemblyVersion"));
                if (!assembly_version_str.empty())
                {
                    version_t::parse(assembly_version_str, &assembly_version);
                }

                const pal::string_t file_version_str = get_optional_property(file.value, _X("fileVersion"));
                if (!file_version_str.empty())
                {
                    version_t::parse(file_version_str, &file_version);
                }

                pal::string_t file_name{file.key.data()};
                deps_asset_t asset(get_filename_without_ext(file_name), file_name, assembly_version, file_version);

                if (trace::is_enabled())
                {
                    trace::info(_X("    %s assemblyVersion=%s fileVersion=%s"),
                        asset.relative_path.c_str(),
                        asset.assembly_version.as_str().c_str(),
                        asset.file_version.as_str().c_str());
                }

                asset_files.push_back(std::move(asset));
            }
        }
    }
}

void deps_json_t::load_framework_dependent(const simdjson::dom::element& json, const pal::string_t& target_name)
{
    process_runtime_targets(json, target_name, &m_rid_assets);
    process_targets(json, target_name, &m_assets);

    auto package_exists = [&](const pal::string_t& package) -> bool {
        return m_rid_assets.libs.count(package) || m_assets.libs.count(package);
    };

    const vec_asset_t empty;
    auto get_relpaths = [&](const pal::string_t& package, size_t asset_type_index, bool* rid_specific) -> const vec_asset_t& {

        *rid_specific = false;

        if (m_rid_assets.libs.count(package) && !m_rid_assets.libs[package][asset_type_index].rid_assets.empty())
        {
            const auto& assets_for_type = m_rid_assets.libs[package][asset_type_index].rid_assets.begin()->second;
            if (!assets_for_type.empty())
            {
                *rid_specific = true;
                return assets_for_type;
            }

            trace::verbose(_X("There were no rid specific %s asset for %s"), deps_entry_t::s_known_asset_types[asset_type_index], package.c_str());
        }

        if (m_assets.libs.count(package))
        {
            return m_assets.libs[package][asset_type_index];
        }

        return empty;
    };

    reconcile_libraries_with_targets(json, package_exists, get_relpaths);
}

void deps_json_t::load_self_contained(const simdjson::dom::element& json, const pal::string_t& target_name)
{
    process_targets(json, target_name, &m_assets);

    auto package_exists = [&](const pal::string_t& package) -> bool {
        return m_assets.libs.count(package);
    };

    auto get_relpaths = [&](const pal::string_t& package, size_t type_index, bool* rid_specific) -> const vec_asset_t& {
        *rid_specific = false;
        return m_assets.libs[package][type_index];
    };

    reconcile_libraries_with_targets(json, package_exists, get_relpaths);
}

bool deps_json_t::has_package(const pal::string_t& name, const pal::string_t& ver) const
{
    pal::string_t pv;
    pv.reserve(name.length() + ver.length() + 1);
    pv.assign(name);
    pv.push_back(_X('/'));
    pv.append(ver);

    auto iter = m_rid_assets.libs.find(pv);
    if (iter != m_rid_assets.libs.end())
    {
        for (size_t asset_type_index = 0; asset_type_index < deps_entry_t::asset_types::count; asset_type_index++)
        {
            if (!iter->second[asset_type_index].rid_assets.empty())
            {
                return true;
            }
        }
    }

    return m_assets.libs.count(pv);
}

// -----------------------------------------------------------------------------
// Load the deps file and parse its "entry" lines which contain the "fields" of
// the entry. Populate an array of these entries.
//
void deps_json_t::load(bool is_framework_dependent, std::function<void(const simdjson::dom::element&)> post_process)
{
    m_file_exists = deps_file_exists(m_deps_file);

    if (!m_file_exists)
    {
        // Not existing is valid
        m_valid = true;
        return;
    }

    json_parser_t json;
    if (!json.parse_file(m_deps_file))
        return;

    m_valid = true;
    auto runtime_target = json.document()[_X("runtimeTarget")];

    pal::string_t name;
    if (runtime_target.is_string())
    {
        name = runtime_target.get_string().value().data();
    }
    else
    {
        name = runtime_target[_X("name")].get_string().value().data();
    }

    trace::verbose(_X("Loading deps file... [%s]: is_framework_dependent=%d, use_fallback_graph=%d"), m_deps_file.c_str(), is_framework_dependent, m_rid_resolution_options.use_fallback_graph);

    if (is_framework_dependent)
    {
        load_framework_dependent(json.document(), name);
    }
    else
    {
        load_self_contained(json.document(), name);
    }

    if (post_process)
        post_process(json.document());
}

std::unique_ptr<deps_json_t> deps_json_t::create_for_self_contained(const pal::string_t& deps_path, rid_resolution_options_t& rid_resolution_options)
{
    std::unique_ptr<deps_json_t> deps = std::unique_ptr<deps_json_t>(new deps_json_t(deps_path, rid_resolution_options));
    
    if (rid_resolution_options.use_fallback_graph)
    {
        assert(rid_resolution_options.rid_fallback_graph != nullptr && rid_resolution_options.rid_fallback_graph->empty());
        deps->load(false, [&](const simdjson::dom::element& json)
        {
            populate_rid_fallback_graph(json, *rid_resolution_options.rid_fallback_graph);
        });
    }
    else
    {
        deps->load(false);
    }

    return deps;
}

std::unique_ptr<deps_json_t> deps_json_t::create_for_framework_dependent(const pal::string_t& deps_path, const rid_resolution_options_t& rid_resolution_options)
{
    std::unique_ptr<deps_json_t> deps = std::unique_ptr<deps_json_t>(new deps_json_t(deps_path, rid_resolution_options));
    deps->load(true);
    return deps;
}
