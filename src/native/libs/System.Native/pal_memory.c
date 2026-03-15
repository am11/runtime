// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "pal_config.h"
#include "pal_memory.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#if HAVE_MALLOC_SIZE
    #include <malloc/malloc.h>
    #define MALLOC_SIZE(s) malloc_size(s)
#elif HAVE_MALLOC_USABLE_SIZE
    #include <malloc.h>
    #define MALLOC_SIZE(s) malloc_usable_size(s)
#elif HAVE_MALLOC_USABLE_SIZE_NP
    #include <malloc_np.h>
    #define MALLOC_SIZE(s) malloc_usable_size(s)
#elif defined(TARGET_OPENBSD) || defined(TARGET_SUNOS)
    /* OpenBSD/SunOS: Manual header at (s)[-2] to store the size.
       Check for NULL to ensure MALLOC_SIZE(NULL) returns 0 for the assert. */
    #define MALLOC_SIZE(s) ((s) == NULL ? 0 : (((size_t*)(s))[-2]))
#else
    #error "Platform doesn't support malloc_usable_size or malloc_size"
#endif

void* SystemNative_AlignedAlloc(uintptr_t alignment, uintptr_t size)
{
#if defined(TARGET_OPENBSD) || defined(TARGET_SUNOS)
    /* We need at least 16 bytes for metadata (size_t + void*). */
    size_t reserved = (alignment < 16) ? 16 : alignment;
    void* real_ptr = malloc(size + reserved);
    if (real_ptr == NULL) return NULL;

    /* Round down from the end of the reserved block to find the aligned user_ptr. */
    uintptr_t user_addr = ((uintptr_t)real_ptr + reserved) & ~(alignment - 1);
    void* user_ptr = (void*)user_addr;

    /* Store metadata immediately before user_ptr */
    ((void**)user_ptr)[-1] = real_ptr; /* Store for AlignedFree */
    ((size_t*)user_ptr)[-2] = size;     /* Store for MALLOC_SIZE */
    return user_ptr;
#elif HAVE_ALIGNED_ALLOC
    return aligned_alloc(alignment, size);
#elif HAVE_POSIX_MEMALIGN
    void* result = NULL;
    posix_memalign(&result, alignment, size);
    return result;
#else
    #error "Platform doesn't support aligned_alloc or posix_memalign"
#endif
}

void SystemNative_AlignedFree(void* ptr)
{
#if defined(TARGET_OPENBSD) || defined(TARGET_SUNOS)
    if (ptr != NULL)
    {
        free(((void**)ptr)[-1]);
    }
#else
    free(ptr);
#endif
}

void* SystemNative_AlignedRealloc(void* ptr, uintptr_t alignment, uintptr_t new_size)
{
    void* result = SystemNative_AlignedAlloc(alignment, new_size);

    if (result != NULL)
    {
        uintptr_t old_size = MALLOC_SIZE(ptr);
        assert((ptr != NULL) || (old_size == 0));

        memcpy(result, ptr, (new_size < old_size) ? new_size : old_size);
        SystemNative_AlignedFree(ptr);
    }

    return result;
}

void* SystemNative_Calloc(uintptr_t num, uintptr_t size)
{
    return calloc(num, size);
}

void SystemNative_Free(void* ptr)
{
    free(ptr);
}

void* SystemNative_Malloc(uintptr_t size)
{
    return malloc(size);
}

void* SystemNative_Realloc(void* ptr, uintptr_t new_size)
{
    return realloc(ptr, new_size);
}
