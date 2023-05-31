// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef HAVE_MINIPAL_UTF8_H
#define HAVE_MINIPAL_UTF8_H

#include <minipal/utils.h>
#include <stdlib.h>
#include <stdbool.h>

#define MINIPAL_MB_ERR_INVALID_CHARS 0x00000008
#define MINIPAL_ERROR_INSUFFICIENT_BUFFER 122L
#define MINIPAL_ERROR_INVALID_PARAMETER 87L

#ifdef __cplusplus
extern "C"
{
#endif // __cplusplus

#ifdef TARGET_WINDOWS
typedef wchar_t CHAR16_T;
#else
typedef unsigned short CHAR16_T;
#endif

int minipal_utf8_to_utf16_preallocated(const char* source, size_t sourceLength, CHAR16_T** destination, size_t destinationLength, unsigned int flags
#if BIGENDIAN
    , bool treatAsLE
#endif
);

int minipal_utf16_to_utf8_preallocated(const CHAR16_T* source, size_t sourceLength, char** destination, size_t destinationLength);

int minipal_utf8_to_utf16_allocate(const char* source, size_t sourceLength, CHAR16_T** destination, unsigned int flags
#if BIGENDIAN
    , bool treatAsLE
#endif
);

int minipal_utf16_to_utf8_allocate(const CHAR16_T* source, size_t sourceLength, char** destination
#if BIGENDIAN
    , bool treatAsLE
#endif
);

#ifdef __cplusplus
}
#endif // __cplusplus

#endif /* HAVE_MINIPAL_UTF8_H */
