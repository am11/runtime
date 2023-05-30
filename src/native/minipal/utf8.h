// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef HAVE_MINIPAL_UTF8_H
#define HAVE_MINIPAL_UTF8_H

#include <minipal/utils.h>
#include <stdlib.h>
#include <stdbool.h>

#define MB_ERR_INVALID_CHARS 0x00000008
#define ERROR_NO_UNICODE_TRANSLATION 1113L
#define ERROR_INSUFFICIENT_BUFFER 122L
#define ERROR_INVALID_PARAMETER 87L

#ifdef __cplusplus
extern "C"
{
#endif // __cplusplus

#ifdef TARGET_WINDOWS
typedef wchar_t CHAR16_T;
#else
typedef unsigned short CHAR16_T;
#endif

int minipal_utf8_to_utf16_preallocated(const char* lpSrcStr, int cchSrc, CHAR16_T** lpDestStr, int cchDest, unsigned int dwFlags
#if BIGENDIAN
    bool treatAsLE
#endif
);

int minipal_utf16_to_utf8_preallocated(const CHAR16_T* lpSrcStr, int cchSrc, char** lpDestStr, int cchDest);

int minipal_utf8_to_utf16_allocate(const char* lpSrcStr, int cchSrc, CHAR16_T** lpDestStr, unsigned int dwFlags
#if BIGENDIAN
    , bool treatAsLE
#endif
);

int minipal_utf16_to_utf8_allocate(const CHAR16_T* lpSrcStr, int cchSrc, char** lpDestStr
#if BIGENDIAN
    , bool treatAsLE
#endif
);

#ifdef __cplusplus
}
#endif // __cplusplus

#endif /* HAVE_MINIPAL_UTF8_H */
