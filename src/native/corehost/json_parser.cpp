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
    if (result.error())
    {
        trace::error(_X("A JSON parsing exception occurred in [%s]: %s"),
                     context.c_str(), simdjson::error_message(result.error()));
        return false;
    }

    m_document = result.value();

    if (m_document.is_object())
    {
        trace::error(_X("Expected a JSON object in [%s]"), context.c_str());
        return false;
    }

    return true;
}

bool json_parser_t::parse_file(const pal::string_t& path)
{
    // This code assumes that the caller has checked that the file `path` exists
    // either within the bundle, or as a real file on disk.
    assert(m_data == nullptr);
    assert(m_bundle_location == nullptr);

    if (bundle::info_t::is_single_file_bundle())
    {
        // Due to in-situ parsing on Linux,
        //  * The json file is mapped as copy-on-write.
        //  * The mapping cannot be immediately released, and will be unmapped by the json_parser destructor.
        m_data = bundle::info_t::config_t::map(path, m_bundle_location);

        if (m_data != nullptr)
        {
            m_size = (size_t)m_bundle_location->size;
        }
    }

    if (m_data == nullptr)
    {
#ifdef _WIN32
        // We can't use in-situ parsing on Windows, as JSON data is encoded in
        // UTF-8 and the host expects wide strings.
        // We do not need copy-on-write, so read-only mapping will be enough.
        m_data = (char*)pal::mmap_read(path, &m_size);
#else // _WIN32
        m_data = (char*)pal::mmap_copy_on_write(path, &m_size);
#endif // _WIN32

        if (m_data == nullptr)
        {
            trace::error(_X("Cannot use file stream for [%s]: %s"), path.c_str(), pal::strerror(errno).c_str());
            return false;
        }
    }

    char *data = m_data;
    size_t size = m_size;

    // Skip over UTF-8 BOM, if present
    if (size >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[1] == 0xBF)
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
