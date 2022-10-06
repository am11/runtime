// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "CommonTypes.h"

void __cxa_pure_virtual() 
{
    printf("__cxa_pure_virtual() called!\n");
    abort();
}

void* operator new(size_t n, const std::nothrow_t&) noexcept
{
    return malloc(n);
}

void* operator new[](size_t n, const std::nothrow_t&) noexcept
{
    return malloc(n);
}

void operator delete(void *p) noexcept
{
    free(p);
}

void operator delete[](void *p) noexcept
{
    free(p);
}
