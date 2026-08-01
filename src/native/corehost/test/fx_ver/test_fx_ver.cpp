// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "fx_ver.h"
#include "pal.h"

#define TEST_ASSERT(a) \
  if (!(a)) \
  { \
    fprintf(stderr, "TEST_ASSERT failed '%s' at %d\n", #a, __LINE__); \
    exit(1); \
  }

struct TestCase
{
    pal::string_t str;
    struct
    {
        int major;
        int minor;
        int patch;
        pal::string_t pre;
        pal::string_t build;
    } ver;
    bool same;
};

const TestCase orderedCases[] =
{
    { PAL_X("1.0.0-0.3.7"),                { 1, 0, 0,  PAL_X("-0.3.7"),             PAL_X("") }                 , false },
    { PAL_X("1.0.0-alpha"),                { 1, 0, 0,  PAL_X("-alpha"),             PAL_X("") }                 , false },
    { PAL_X("1.0.0-alpha+001"),            { 1, 0, 0,  PAL_X("-alpha"),             PAL_X("+001") }             , true  },
    { PAL_X("1.0.0-alpha.1"),              { 1, 0, 0,  PAL_X("-alpha.1"),           PAL_X("") }                 , false },
    { PAL_X("1.0.0-alpha.beta"),           { 1, 0, 0,  PAL_X("-alpha.beta"),        PAL_X("") }                 , false },
    { PAL_X("1.0.0-beta"),                 { 1, 0, 0,  PAL_X("-beta"),              PAL_X("") }                 , false },
    { PAL_X("1.0.0-beta+exp.sha.5114f85"), { 1, 0, 0,  PAL_X("-beta"),              PAL_X("+exp.sha.5114f85") } , true  },
    { PAL_X("1.0.0-beta.2"),               { 1, 0, 0,  PAL_X("-beta.2"),            PAL_X("") }                 , false },
    { PAL_X("1.0.0-beta.11"),              { 1, 0, 0,  PAL_X("-beta.11"),           PAL_X("") }                 , false },
    { PAL_X("1.0.0-rc.1"),                 { 1, 0, 0,  PAL_X("-rc.1"),              PAL_X("") }                 , false },
    { PAL_X("1.0.0-x.7.z.92"),             { 1, 0, 0,  PAL_X("-x.7.z.92"),          PAL_X("") }                 , false },
    { PAL_X("1.0.0"),                      { 1, 0, 0,  PAL_X(""),                   PAL_X("") }                 , false },
    { PAL_X("1.0.0+20130313144700"),       { 1, 0, 0,  PAL_X(""),                   PAL_X("+20130313144700") }  , true  },
    { PAL_X("1.9.0-9"),                    { 1, 9, 0,  PAL_X("-9"),                 PAL_X("") }                 , false },
    { PAL_X("1.9.0-10"),                   { 1, 9, 0,  PAL_X("-10"),                PAL_X("") }                 , false },
    { PAL_X("1.9.0-1A"),                   { 1, 9, 0,  PAL_X("-1A"),                PAL_X("") }                 , false },
    { PAL_X("1.9.0"),                      { 1, 9, 0,  PAL_X(""),                   PAL_X("") }                 , false },
    { PAL_X("1.10.0"),                     { 1, 10, 0, PAL_X(""),                   PAL_X("") }                 , false },
    { PAL_X("1.11.0"),                     { 1, 11, 0, PAL_X(""),                   PAL_X("") }                 , false },
    { PAL_X("2.0.0"),                      { 2, 0, 0,  PAL_X(""),                   PAL_X("") }                 , false },
    { PAL_X("2.1.0"),                      { 2, 1, 0,  PAL_X(""),                   PAL_X("") }                 , false },
    { PAL_X("2.1.1"),                      { 2, 1, 1,  PAL_X(""),                   PAL_X("") }                 , false },
    { PAL_X("4.6.0-preview.19064.1"),      { 4, 6, 0,  PAL_X("-preview.19064.1"),   PAL_X("") }                 , false },
    { PAL_X("4.6.0-preview1-27018-01"),    { 4, 6, 0,  PAL_X("-preview1-27018-01"), PAL_X("") }                 , false },
};

const size_t cases = sizeof(orderedCases)/sizeof(TestCase);

void checkPrecedence(size_t iVal, const fx_ver_t &iVer, size_t jVal, const fx_ver_t &jVer)
{
    if (iVal == jVal)
    {
        TEST_ASSERT( (iVer == jVer));
        TEST_ASSERT(!(iVer <  jVer));
        TEST_ASSERT(!(iVer >  jVer));
        TEST_ASSERT( (iVer <= jVer));
        TEST_ASSERT( (iVer >= jVer));
        TEST_ASSERT(!(iVer != jVer));
    }
    else if (iVal < jVal)
    {
        TEST_ASSERT(!(iVer == jVer));
        TEST_ASSERT( (iVer <  jVer));
        TEST_ASSERT(!(iVer >  jVer));
        TEST_ASSERT( (iVer <= jVer));
        TEST_ASSERT(!(iVer >= jVer));
        TEST_ASSERT( (iVer != jVer));
    }
    else
    {
        TEST_ASSERT(iVal > jVal);

        TEST_ASSERT(!(iVer == jVer));
        TEST_ASSERT(!(iVer <  jVer));
        TEST_ASSERT( (iVer >  jVer));
        TEST_ASSERT(!(iVer <= jVer));
        TEST_ASSERT( (iVer >= jVer));
        TEST_ASSERT( (iVer != jVer));
    }
}

void checkPrecedence()
{
    size_t isame = 0;

    for (size_t i = 0; i < cases; ++i)
    {
        fx_ver_t iver;
        bool ivalid = fx_ver_t::parse(orderedCases[i].str, &iver);

        TEST_ASSERT(ivalid);

        if (orderedCases[i].same) isame++;

        size_t jsame = 0;

        for (size_t j = 0; j < cases; ++j)
        {
            fx_ver_t jver;
            bool jvalid = fx_ver_t::parse(orderedCases[j].str, &jver);

            TEST_ASSERT(jvalid);

            if (orderedCases[j].same) jsame++;

            checkPrecedence(i - isame, iver, j - jsame, jver);
        }
    }
}

void checkParsing(const TestCase &myCase)
{
    fx_ver_t ver;
    bool valid = fx_ver_t::parse(myCase.str, &ver);

    TEST_ASSERT(valid);
    TEST_ASSERT(ver.get_major() == myCase.ver.major);
    TEST_ASSERT(ver.get_minor() == myCase.ver.minor);
    TEST_ASSERT(ver.get_patch() == myCase.ver.patch);
    TEST_ASSERT(ver.is_prerelease() == !myCase.ver.pre.empty());
    TEST_ASSERT(ver.as_str() == myCase.str);
}

void checkParsing()
{
    for (size_t i = 0; i < cases; ++i)
    {
        checkParsing(orderedCases[i]);
    }
}

void checkInvalidVersions()
{
    pal::string_t invalidVersions[] =
    {
        PAL_X(""),
        PAL_X("1"),
        PAL_X("1.1"),
        PAL_X("A.1.1"),
        PAL_X("1.A.1"),
        PAL_X("1.1.A"),
        PAL_X("1A.1.1"),
        PAL_X("1.1A.1"),
        PAL_X("1.1.1A"),
        PAL_X("1.1.1-"),
        PAL_X("1.1.1-."),
        PAL_X("1.1.1-A."),
        PAL_X("1.1.1-A.B."),
        PAL_X("1.1.1-.+id"),
        PAL_X("1.1.1-A.+id"),
        PAL_X("1.1.1-A.B.+id"),
        PAL_X("1.1.1-A.B+id."),
        PAL_X("01.1.1"),
        PAL_X("1.01.1"),
        PAL_X("1.1.01"),
        PAL_X("1.1.1-01.B"),
        PAL_X("1.1.1-A.01"),
        PAL_X("00.1.1"),
        PAL_X("1.00.1"),
        PAL_X("1.1.00"),
        PAL_X("1.1.00-A"),
        PAL_X("1.1.1-00.B"),
        PAL_X("1.1.1-A.00"),
        PAL_X("1.1.1+"),
        PAL_X("1.1.1-A+"),
        PAL_X("1.1.1-A*B"),
        PAL_X("1.1.1-A/B"),
        PAL_X("1.1.1-A:B"),
        PAL_X("1.1.1-A^B"),
        PAL_X("1.1.1-A|B"),
    };

    const size_t invalid_cases = sizeof(invalidVersions)/sizeof(pal::string_t);

    for (size_t i = 0; i < invalid_cases; ++i)
    {
        fx_ver_t ver;
        bool valid = fx_ver_t::parse(invalidVersions[i], &ver);

        TEST_ASSERT(!valid);
    }
}

#if defined(_WIN32)
int __cdecl wmain(const int argc, const pal::char_t* argv[])
#else
int main(const int argc, const pal::char_t* argv[])
#endif
{
    checkInvalidVersions();
    checkParsing();
    checkPrecedence();
}
