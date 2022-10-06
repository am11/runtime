// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef __COMMON_TYPES_H__
#define __COMMON_TYPES_H__

#include <cstddef>
#include <cstdint>
#include <stdlib.h>
#include <stdio.h>

#ifdef TARGET_WINDOWS

#include <new>

#else

extern "C" void __cxa_pure_virtual();

namespace std
{
   struct nothrow_t {};
   const std::nothrow_t nothrow;
}

void* operator new(size_t n, const std::nothrow_t&) noexcept;
void* operator new[](size_t n, const std::nothrow_t&) noexcept;
void operator delete(void *p) noexcept;
void operator delete[](void *p) noexcept;

#endif

using std::nothrow;
using std::size_t;
using std::uintptr_t;
using std::intptr_t;

typedef wchar_t             WCHAR;
typedef void *              HANDLE;

typedef uint32_t            UInt32_BOOL;    // windows 4-byte BOOL, 0 -> false, everything else -> true
#define UInt32_FALSE        0
#define UInt32_TRUE         1

#endif // __COMMON_TYPES_H__
