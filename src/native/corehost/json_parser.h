// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef __JSON_PARSER_H__
#define __JSON_PARSER_H__

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#endif

#include "pal.h"
#include <external/simdjson/simdjson.h>
#include <vector>
#include "bundle/info.h"

#undef GetObject

class json_parser_t
{
    public:
        bool parse_raw_data(char* data, int64_t size, const pal::string_t& context);
        bool parse_file(const pal::string_t& path);

        const simdjson::dom::element& document() const { return m_document; }

        json_parser_t()
            : m_data(nullptr)
            , m_bundle_location(nullptr) {}

        ~json_parser_t();

    private:
        char* m_data; // The memory mapped bytes of the file
        size_t m_size; // Size of the mapped memory

        // On Windows, where wide strings are used, m_data is kept in UTF-8, but converted
        // to UTF-16 by m_document during load.
        simdjson::dom::element m_document;

        // If a json file is parsed from a single-file bundle, the following fields represents
        // the location of this json file within the bundle.
        const bundle::location_t* m_bundle_location;
};

#endif // __JSON_PARSER_H__
