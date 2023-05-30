// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*============================================================================
**
** Source: test4.c
**
** Purpose: Tests WideCharMultiByte with UTF-8 encoding
**
**
**==========================================================================*/

#include <palsuite.h>

PALTEST(locale_info_WideCharToMultiByte_test5_paltest_widechartomultibyte_test5, "locale_info/WideCharToMultiByte/test5/paltest_widechartomultibyte_test5")
{    
    int ret;
    int ret2;

    if (PAL_Initialize(argc, argv))
    {
        return FAIL;
    }

    const WCHAR * const unicodeStrings[] =
    {
        // Single high surrogate
        W("\xD800"),
    };
    
    const char * const utf8Strings[] =
    {
        "\xEF\xBF\xBD",
    };

    for (int i = 0; i < (sizeof(unicodeStrings) / sizeof(unicodeStrings[0])); i++)
    {
        ret = WideCharToMultiByte(CP_UTF8, 0, unicodeStrings[i], -1, NULL, 0, NULL, NULL);
        CHAR* utf8Buffer = (CHAR*)malloc(ret * sizeof(CHAR));
        ret2 = WideCharToMultiByte(CP_UTF8, 0, unicodeStrings[i], -1, utf8Buffer, ret, NULL, NULL);
        if (ret != ret2)
        {
            Fail("WideCharToMultiByte string %d: returned different string length for empty and real dest buffers!\n"
                "Got %d for the empty one, %d for real one.\n", i, ret2, ret);
        }
        
        if (strcmp(utf8Buffer, utf8Strings[i]) != 0)
        {
            Fail("WideCharToMultiByte string %d: the resulting string doesn't match the expected one!\n", i);
        }
        
        free(utf8Buffer);
    }
   
    PAL_Terminate();

    return PASS;
}
