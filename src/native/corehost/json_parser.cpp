// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <json_parser.h>
#include "utils.h"
#include <cassert>
#include <cstdint>

namespace {

void get_line_column_from_offset(const char* data, uint64_t size, size_t offset, int *line, int *column)
{
    assert(offset <= size);

    *line = *column = 1;

    for (size_t i = 0; i < offset; i++)
    {
        (*column)++;

        if (data[i] == '\n')
        {
            (*line)++;
            *column = 1;
        }
        else if (data[i] == '\r' && data[i + 1] == '\n')
        {
            (*line)++;
            *column = 1;

            i++; // Discard carriage return
        }
    }
}

} // empty namespace

bool json_parser_t::parse_raw_data(char* data, int64_t size, const pal::string_t& context)
{
    assert(data != nullptr);

    // simdjson will parse in-situ, so the input buffer must remain valid.
    simdjson::dom::parser m_parser;
    auto result = m_parser.parse(data, size);
    if (result.error()) {
        trace::error(_X("A JSON parsing exception occurred in [%s]: %s"),
                     context.c_str(), simdjson::error_message(result.error()));
        return false;
    }

    m_document = result.value();

    // Ensure the parsed document is an object.
    if (m_document.type() != simdjson::dom::element_type::OBJECT) {
        trace::error(_X("Expected a JSON object in [%s]"), context.c_str());
        return false;
    }

    return true;
}

bool json_parser_t::parse_file(const pal::string_t& path)
{
    // Pre-conditions: no previous mapping.
    assert(m_data == nullptr);
    assert(m_bundle_location == nullptr);

    if (bundle::info_t::is_single_file_bundle())
    {
        // For single-file bundles the JSON file is memory-mapped.
        m_data = bundle::info_t::config_t::map(path, m_bundle_location);
        if (m_data != nullptr) {
            m_size = (size_t)m_bundle_location->size;
        }
    }

    // If not in a bundle, map the file from disk.
    if (m_data == nullptr)
    {
#ifdef _WIN32
        // On Windows we use a read-only mapping.
        m_data = (char*)pal::mmap_read(path, &m_size);
#else
        // On Linux/macOS, use copy-on-write mapping for in-situ parsing.
        m_data = (char*)pal::mmap_copy_on_write(path, &m_size);
#endif
        if (m_data == nullptr) {
            trace::error(_X("Cannot use file stream for [%s]: %s"),
                         path.c_str(), pal::strerror(errno).c_str());
            return false;
        }
    }

    char* data = m_data;
    size_t size = m_size;

    // Skip over the UTF-8 BOM if present.
    if (size >= 3 && (unsigned char)data[0] == 0xEF &&
                      (unsigned char)data[1] == 0xBB &&
                      (unsigned char)data[2] == 0xBF)
    {
        size -= 3;
        data += 3;
    }

    return parse_raw_data(data, size, path);
}

json_parser_t::~json_parser_t()
{
    if (m_data != nullptr)
    {
        if (m_bundle_location != nullptr)
        {
            bundle::info_t::config_t::unmap(m_data, m_bundle_location);
        }
        else
        {
            pal::munmap((void*)m_data, m_size);
        }
    }
}
