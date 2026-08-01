// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "fx_resolver.h"
#include "framework_info.h"
#include "install_info.h"

/**
* When the framework is referenced more than once in a non-compatible way, display detailed error message
*   about available frameworks and installation of new framework.
*/
void fx_resolver_t::display_incompatible_framework_error(
    const pal::string_t& higher,
    const fx_reference_t& lower)
{
    trace::error(PAL_X("The specified framework '%s', version '%s', apply_patches=%d, version_compatibility_range=%s cannot roll-forward to the previously referenced version '%s'."),
        lower.get_fx_name().c_str(),
        lower.get_fx_version().c_str(),
        lower.get_apply_patches(),
        version_compatibility_range_to_string(lower.get_version_compatibility_range()).c_str(),
        higher.c_str());
}

void fx_resolver_t::display_compatible_framework_trace(
    const pal::string_t& higher,
    const fx_reference_t& lower)
{
    if (trace::is_enabled())
    {
        trace::verbose(PAL_X("--- The specified framework '%s', version '%s', apply_patches=%d, version_compatibility_range=%s is compatible with the previously referenced version '%s'."),
            lower.get_fx_name().c_str(),
            lower.get_fx_version().c_str(),
            lower.get_apply_patches(),
            version_compatibility_range_to_string(lower.get_version_compatibility_range()).c_str(),
            higher.c_str());
    }
}

void fx_resolver_t::display_retry_framework_trace(
    const fx_reference_t& fx_existing,
    const fx_reference_t& fx_new)
{
    if (trace::is_enabled())
    {
        trace::verbose(PAL_X("--- Restarting all framework resolution because the previously resolved framework '%s', version '%s' must be re-resolved with the new version '%s', apply_patches=%d, version_compatibility_range=%s, roll_to_highest_version=%d ."),
            fx_existing.get_fx_name().c_str(),
            fx_existing.get_fx_version().c_str(),
            fx_new.get_fx_version().c_str(),
            fx_new.get_apply_patches(),
            version_compatibility_range_to_string(fx_new.get_version_compatibility_range()).c_str(),
            fx_new.get_roll_to_highest_version());
    }
}

void fx_resolver_t::display_summary_of_frameworks(
    const fx_definition_vector_t& fx_definitions,
    const fx_name_to_fx_reference_map_t& newest_references)
{
    if (trace::is_enabled())
    {
        trace::verbose(PAL_X("--- Summary of all frameworks:"));

        bool is_app = true;
        for (const auto& fx : fx_definitions)
        {
            if (is_app)
            {
                is_app = false; // skip the app
            }
            else
            {
                auto newest_ref = newest_references.find(fx->get_name());
                assert(newest_ref != newest_references.end());

                trace::verbose(PAL_X("     framework:'%s', lowest requested version='%s', found version='%s', effective reference version='%s' apply_patches=%d, version_compatibility_range=%s, roll_to_highest_version=%d, folder=%s"),
                    fx->get_name().c_str(),
                    fx->get_requested_version().c_str(),
                    fx->get_found_version().c_str(),
                    newest_ref->second.get_fx_version().c_str(),
                    newest_ref->second.get_apply_patches(),
                    version_compatibility_range_to_string(newest_ref->second.get_version_compatibility_range()).c_str(),
                    newest_ref->second.get_roll_to_highest_version(),
                    fx->get_dir().c_str());
            }
        }
    }
}

/**
* When the framework is not found, display detailed error message
*   about available frameworks and installation of new framework.
*/
void fx_resolver_t::display_missing_framework_error(
    const pal::string_t& fx_name,
    const pal::string_t& fx_version,
    const pal::string_t& dotnet_root,
    bool disable_multilevel_lookup)
{

    // Display the error message about missing FX.
    if (fx_version.length())
    {
        trace::error(PAL_X("Framework: '%s', version '%s' (%s)"), fx_name.c_str(), fx_version.c_str(), get_current_arch_name());
    }
    else
    {
        trace::error(PAL_X("Framework: '%s', (%s)"), fx_name.c_str(), get_current_arch_name());
    }

    trace::error(PAL_X(".NET location: %s\n"), dotnet_root.c_str());

    std::vector<framework_info> framework_infos;
    framework_info::get_all_framework_infos(dotnet_root, fx_name.c_str(), disable_multilevel_lookup, /*include_disabled_versions*/ true, &framework_infos);
    if (framework_infos.size())
    {
        trace::error(PAL_X("The following frameworks were found:"));
        for (const framework_info& info : framework_infos)
        {
            trace::error(PAL_X("  %s at [%s]"), info.version.as_str().c_str(), info.path.c_str());
            if (info.disabled)
            {
                trace::error(PAL_X("    Disabled via DOTNET_DISABLE_RUNTIME_VERSIONS environment variable"));
            }
        }
    }
    else
    {
        trace::error(PAL_X("No frameworks were found."));
    }

    std::vector<std::pair<pal::architecture, std::vector<framework_info>>> other_arch_framework_infos;
    install_info::enumerate_other_architectures(
        [&](pal::architecture arch, const pal::string_t& install_location, bool is_registered)
        {
            std::vector<framework_info> other_arch_infos;
            framework_info::get_all_framework_infos(install_location, fx_name.c_str(), disable_multilevel_lookup, /*include_disabled_versions*/ true, &other_arch_infos);
            if (!other_arch_infos.empty())
            {
                other_arch_framework_infos.push_back(std::make_pair(arch, std::move(other_arch_infos)));
            }
        });
    if (!other_arch_framework_infos.empty())
    {
        trace::error(PAL_X("\nThe following frameworks for other architectures were found:"));
        for (const auto& arch_info_pair : other_arch_framework_infos)
        {
            trace::error(PAL_X("  %s"), get_arch_name(arch_info_pair.first));
            for (const framework_info& info : arch_info_pair.second)
            {
                trace::error(PAL_X("    %s at [%s]"), info.version.as_str().c_str(), info.path.c_str());
                if (info.disabled)
                {
                    trace::error(PAL_X("      Disabled via DOTNET_DISABLE_RUNTIME_VERSIONS environment variable"));
                }
            }
        }
    }

    pal::string_t url = get_download_url(fx_name.c_str(), fx_version.c_str());
    trace::error(
        PAL_X("\n")
        DOC_LINK_INTRO PAL_X("\n")
        DOTNET_APP_LAUNCH_FAILED_URL
        PAL_X("\n\n")
        PAL_X("To install missing framework, download:\n")
        PAL_X("%s"),
        url.c_str());
}

void fx_resolver_t::display_incompatible_loaded_framework_error(
    const pal::string_t& loaded_version,
    const fx_reference_t& fx_ref)
{
    trace::error(PAL_X("The specified framework '%s', version '%s', apply_patches=%d, version_compatibility_range=%s is incompatible with the previously loaded version '%s'."),
        fx_ref.get_fx_name().c_str(),
        fx_ref.get_fx_version().c_str(),
        fx_ref.get_apply_patches(),
        version_compatibility_range_to_string(fx_ref.get_version_compatibility_range()).c_str(),
        loaded_version.c_str());
}

void fx_resolver_t::display_missing_loaded_framework_error(const pal::string_t& fx_name)
{
    trace::error(PAL_X("The specified framework '%s' is not present in the previously loaded runtime."), fx_name.c_str());
}
