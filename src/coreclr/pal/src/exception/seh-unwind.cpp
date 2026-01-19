// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++
Module Name:
    seh-unwind.cpp

Abstract:
    Implementation of exception API functions based on
    the Unwind API.
--*/

#ifdef HOST_UNIX
#include "pal/context.h"
#include "pal.h"
#include <dlfcn.h>
#else // HOST_UNIX
#include <windows.h>
#define __STDC_FORMAT_MACROS
#include <inttypes.h>
#include "debugmacros.h"
#include "crosscomp.h"

#define KNONVOLATILE_CONTEXT_POINTERS T_KNONVOLATILE_CONTEXT_POINTERS
#define CONTEXT T_CONTEXT

#define ASSERT(x, ...)
#define TRACE(x, ...)

#define PALAPI

#endif // HOST_UNIX

#ifndef HOST_WINDOWS

struct ExceptionRecords
{
    CONTEXT ContextRecord;
    EXCEPTION_RECORD ExceptionRecord;
};

// Max number of fallback contexts that are used when malloc fails to allocate ExceptionRecords structure
static const int MaxFallbackContexts = sizeof(size_t) * 8;
// Array of fallback contexts
static ExceptionRecords s_fallbackContexts[MaxFallbackContexts];
// Bitmap used for allocating fallback contexts - bits set to 1 represent already allocated context.
static volatile size_t s_allocatedContextsBitmap = 0;

/*++
Function:
    AllocateExceptionRecords

    Allocate EXCEPTION_RECORD and CONTEXT structures for an exception.
Parameters:
    exceptionRecord - output pointer to the allocated exception record
    contextRecord - output pointer to the allocated context record
--*/
VOID
AllocateExceptionRecords(EXCEPTION_RECORD** exceptionRecord, CONTEXT** contextRecord)
{
    ExceptionRecords* records;
    if (posix_memalign((void**)&records, alignof(ExceptionRecords), sizeof(ExceptionRecords)) != 0)
    {
        size_t bitmap;
        size_t newBitmap;
        int index;

        do
        {
            bitmap = s_allocatedContextsBitmap;
            index = __builtin_ffsl(~bitmap) - 1;
            if (index < 0)
            {
                PROCAbort();
            }

            newBitmap = bitmap | ((size_t)1 << index);
        }
        while (__sync_val_compare_and_swap(&s_allocatedContextsBitmap, bitmap, newBitmap) != bitmap);

        records = &s_fallbackContexts[index];
    }

    *contextRecord = &records->ContextRecord;
    *exceptionRecord = &records->ExceptionRecord;
}

/*++
Function:
    PAL_FreeExceptionRecords

    Free EXCEPTION_RECORD and CONTEXT structures of an exception that were allocated by the
    AllocateExceptionRecords.
Parameters:
    exceptionRecord - exception record
    contextRecord - context record
--*/
VOID
PALAPI
PAL_FreeExceptionRecords(IN EXCEPTION_RECORD *exceptionRecord, IN CONTEXT *contextRecord)
{
    // Both records are allocated at once and the allocated memory starts at the contextRecord
    ExceptionRecords* records = (ExceptionRecords*)contextRecord;
    if ((records >= &s_fallbackContexts[0]) && (records < &s_fallbackContexts[MaxFallbackContexts]))
    {
        int index = records - &s_fallbackContexts[0];
        __sync_fetch_and_and(&s_allocatedContextsBitmap, ~((size_t)1 << index));
    }
    else
    {
        free(contextRecord);
    }
}

/*++
Function:
    RtlpRaiseException

Parameters:
    ExceptionRecord - the Windows exception record to throw

Note:
    The name of this function and the name of the ExceptionRecord
    parameter is used in the sos lldb plugin code to read the exception
    record. See coreclr\tools\SOS\lldbplugin\services.cpp.

    This function must not be inlined or optimized so the below calls end
    up with RaiseException caller's context and so the above debugger
    code finds the function and ExceptionRecord parameter.
--*/
PAL_NORETURN
__attribute__((noinline))
__attribute__((NOOPT_ATTRIBUTE))
static void
RtlpRaiseException(EXCEPTION_RECORD *ExceptionRecord, CONTEXT *ContextRecord)
{
    throw PAL_SEHException(ExceptionRecord, ContextRecord);
}

/*++
Function:
  RaiseException

See MSDN doc.
--*/
// no PAL_NORETURN, as callers must assume this can return for continuable exceptions.
__attribute__((noinline))
VOID
PALAPI
RaiseException(IN DWORD dwExceptionCode,
               IN DWORD dwExceptionFlags,
               IN DWORD nNumberOfArguments,
               IN CONST ULONG_PTR *lpArguments)
{
    // PERF_ENTRY_ONLY is used here because RaiseException may or may not
    // return. We can not get latency data without PERF_EXIT. For this reason,
    // PERF_ENTRY_ONLY is used to profile frequency only.
    PERF_ENTRY_ONLY(RaiseException);
    ENTRY("RaiseException(dwCode=%#x, dwFlags=%#x, nArgs=%u, lpArguments=%p)\n",
          dwExceptionCode, dwExceptionFlags, nNumberOfArguments, lpArguments);

    /* Validate parameters */
    if (dwExceptionCode & RESERVED_SEH_BIT)
    {
        WARN("Exception code %08x has bit 28 set; clearing it.\n", dwExceptionCode);
        dwExceptionCode ^= RESERVED_SEH_BIT;
    }

    if (nNumberOfArguments > EXCEPTION_MAXIMUM_PARAMETERS)
    {
        WARN("Number of arguments (%d) exceeds the limit "
            "EXCEPTION_MAXIMUM_PARAMETERS (%d); ignoring extra parameters.\n",
            nNumberOfArguments, EXCEPTION_MAXIMUM_PARAMETERS);
        nNumberOfArguments = EXCEPTION_MAXIMUM_PARAMETERS;
    }

    CONTEXT *contextRecord;
    EXCEPTION_RECORD *exceptionRecord;
    AllocateExceptionRecords(&exceptionRecord, &contextRecord);

    ZeroMemory(exceptionRecord, sizeof(EXCEPTION_RECORD));

    exceptionRecord->ExceptionCode = dwExceptionCode;
    exceptionRecord->ExceptionFlags = dwExceptionFlags;
    exceptionRecord->ExceptionRecord = NULL;
    exceptionRecord->ExceptionAddress = NULL; // will be set by RtlpRaiseException
    exceptionRecord->NumberParameters = nNumberOfArguments;
    if (nNumberOfArguments)
    {
        CopyMemory(exceptionRecord->ExceptionInformation, lpArguments,
                   nNumberOfArguments * sizeof(ULONG_PTR));
    }

    // Capture the context of RaiseException.
    ZeroMemory(contextRecord, sizeof(CONTEXT));
    // WASM-TODO: reconsider this
#ifndef TARGET_WASM
    contextRecord->ContextFlags = CONTEXT_FULL;
    CONTEXT_CaptureContext(contextRecord);

    // Unwind one level to get the caller's context.
    // RaiseException is a leaf function at this point (CONTEXT_CaptureContext was just called),
#if defined(TARGET_AMD64)
    contextRecord->Rip = *(ULONGLONG*)contextRecord->Rsp;
    contextRecord->Rsp += sizeof(ULONGLONG);
#elif defined(TARGET_X86)
    contextRecord->Eip = *(ULONG*)contextRecord->Esp;
    contextRecord->Esp += sizeof(ULONG);
#elif defined(TARGET_ARM)
    contextRecord->Pc = contextRecord->Lr;
#elif defined(TARGET_ARM64)
    contextRecord->Pc = contextRecord->Lr;
#elif defined(TARGET_LOONGARCH64)
    contextRecord->Pc = contextRecord->Ra;
#elif defined(TARGET_RISCV64)
    contextRecord->Pc = contextRecord->Ra;
#else
#error "Unsupported target architecture"
#endif
#endif // !TARGET_WASM

    exceptionRecord->ExceptionAddress = (void *)CONTEXTGetPC(contextRecord);

    RtlpRaiseException(exceptionRecord, contextRecord);

    LOGEXIT("RaiseException returns\n");
}

#endif // !HOST_WINDOWS
