// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++



Module Name:

    seh.cpp

Abstract:

    Implementation of exception API functions.



--*/

#include "pal/thread.hpp"
#include "pal/handleapi.hpp"
#include "pal/seh.hpp"
#include "pal/dbgmsg.h"
#include "pal/debug.h"
#include "pal/init.h"
#include "pal/process.h"
#include "pal/signal.hpp"

#if HAVE_MACH_EXCEPTIONS
#include "machexception.h"
#else
#include <signal.h>
#endif

#include <string.h>
#include <unistd.h>
#include <pthread.h>
#include <stdlib.h>
#include <utility>

using namespace CorUnix;

SET_DEFAULT_DEBUG_CHANNEL(EXCEPT);

/* Internal variables definitions **********************************************/

PHARDWARE_EXCEPTION_HANDLER g_hardwareExceptionHandler = NULL;
// Function to check if an activation can be safely injected at a specified context
PHARDWARE_EXCEPTION_SAFETY_CHECK_FUNCTION g_safeExceptionCheckFunction = NULL;

PGET_GCMARKER_EXCEPTION_CODE g_getGcMarkerExceptionCode = NULL;

// Return address of the SEHProcessException, which is used to enable walking over
// the signal handler trampoline on some Unixes where the libunwind cannot do that.
void* g_SEHProcessExceptionReturnAddress = NULL;
void* g_InvokeActivationHandlerReturnAddress = NULL;

/* Internal function definitions **********************************************/

/*++
Function :
    SEHInitialize

    Initialize all SEH-related stuff (signals, etc)

Parameters :
    CPalThread * pthrCurrent : reference to the current thread.
    PAL initialize flags

Return value :
    TRUE  if SEH support initialization succeeded
    FALSE otherwise
--*/
BOOL
SEHInitialize (CPalThread *pthrCurrent, DWORD flags)
{
    if (!SEHInitializeSignals(pthrCurrent, flags))
    {
        ERROR("SEHInitializeSignals failed!\n");
        SEHCleanup();
        return FALSE;
    }

    return TRUE;
}

/*++
Function :
    SEHCleanup

    Undo work done by SEHInitialize

Parameters :
    None

    (no return value)

--*/
VOID
SEHCleanup()
{
    TRACE("Cleaning up SEH\n");

    SEHCleanupSignals(false /* isChildProcess */);
}

/*++
Function:
    PAL_SetHardwareExceptionHandler

    Register a hardware exception handler.

Parameters:
    handler - exception handler

Return value:
    None
--*/
VOID
PALAPI
PAL_SetHardwareExceptionHandler(
    IN PHARDWARE_EXCEPTION_HANDLER exceptionHandler,
    IN PHARDWARE_EXCEPTION_SAFETY_CHECK_FUNCTION exceptionCheckFunction)
{
    g_hardwareExceptionHandler = exceptionHandler;
    g_safeExceptionCheckFunction = exceptionCheckFunction;
}

/*++
Function:
    PAL_SetGetGcMarkerExceptionCode

    Register a function that determines if the specified IP has code that is a GC marker for GCCover.

Parameters:
    getGcMarkerExceptionCode - the function to register

Return value:
    None
--*/
VOID
PALAPI
PAL_SetGetGcMarkerExceptionCode(
    IN PGET_GCMARKER_EXCEPTION_CODE getGcMarkerExceptionCode)
{
    g_getGcMarkerExceptionCode = getGcMarkerExceptionCode;
}

/*++
Function:
    EnsureExceptionRecordsOnHeap

    Helper function to move records from stack to heap.

Parameters:
    PAL_SEHException* exception
--*/
static void EnsureExceptionRecordsOnHeap(PAL_SEHException* exception)
{
    if( !exception->RecordsOnStack ||
        exception->ExceptionPointers.ExceptionRecord == NULL )
    {
        return;
    }

    CONTEXT* contextRecord = exception->ExceptionPointers.ContextRecord;
    EXCEPTION_RECORD* exceptionRecord = exception->ExceptionPointers.ExceptionRecord;

    CONTEXT* contextRecordCopy;
    EXCEPTION_RECORD* exceptionRecordCopy;
    PAL_AllocateExceptionRecords(&exceptionRecordCopy, &contextRecordCopy);

    *exceptionRecordCopy = *exceptionRecord;
    *contextRecordCopy = *contextRecord;

    exception->ExceptionPointers.ExceptionRecord = exceptionRecordCopy;
    exception->ExceptionPointers.ContextRecord = contextRecordCopy;
    exception->RecordsOnStack = false;
}

/*++
Function:
    SEHProcessException

    Send the PAL exception to any handler registered.

Parameters:
    PAL_SEHException* exception

Return value:
    Returns TRUE if the exception happened in managed code and the execution should
    continue (with possibly modified context).
    Returns FALSE if the exception happened in managed code and it was not handled.
    In case the exception was handled by calling a catch handler, it doesn't return at all.
--*/
BOOL
SEHProcessException(PAL_SEHException* exception)
{
    g_SEHProcessExceptionReturnAddress = _ReturnAddress();

    CONTEXT* contextRecord = exception->GetContextRecord();
    EXCEPTION_RECORD* exceptionRecord = exception->GetExceptionRecord();

    if (!IsInDebugBreak(exceptionRecord->ExceptionAddress))
    {
        if (g_hardwareExceptionHandler != NULL)
        {
            _ASSERTE(g_safeExceptionCheckFunction != NULL);
            // Check if it is safe to handle the hardware exception (the exception happened in managed code
            // or in a jitter helper or it is a debugger breakpoint)
            if (g_safeExceptionCheckFunction(contextRecord, exceptionRecord))
            {
                EnsureExceptionRecordsOnHeap(exception);
                if (g_hardwareExceptionHandler(exception))
                {
                    // The exception happened in managed code and the execution should continue.
                    return TRUE;
                }

                // The exception was a single step or a breakpoint and it was not handled by the debugger.
            }
        }
    }

    // Unhandled hardware exception pointers->ExceptionRecord->ExceptionCode at pointers->ExceptionRecord->ExceptionAddress
    return FALSE;
}

/*++
Function :
    SEHEnable

    Enable SEH-related stuff on this thread

Parameters:
    CPalThread * pthrCurrent : reference to the current thread.

Return value :
    TRUE  if enabling succeeded
    FALSE otherwise
--*/
extern "C"
PAL_ERROR SEHEnable(CPalThread *pthrCurrent)
{
#if HAVE_MACH_EXCEPTIONS
    return pthrCurrent->EnableMachExceptions();
#else // HAVE_MACH_EXCEPTIONS
    return NO_ERROR;
#endif // HAVE_MACH_EXCEPTIONS
}

/*++
Function :
    SEHDisable

    Disable SEH-related stuff on this thread

Parameters:
    CPalThread * pthrCurrent : reference to the current thread.

Return value :
    TRUE  if enabling succeeded
    FALSE otherwise
--*/
extern "C"
PAL_ERROR SEHDisable(CPalThread *pthrCurrent)
{
#if HAVE_MACH_EXCEPTIONS
    return pthrCurrent->DisableMachExceptions();
#else // HAVE_MACH_EXCEPTIONS
    return NO_ERROR;
#endif // HAVE_MACH_EXCEPTIONS
}

#if !defined(TARGET_WASI)
// seh-unwind.cpp uses libunwind which is unavailable on wasm32-wasip2.
// The WASI build provides equivalent stubs (PAL_VirtualUnwind, RtlCaptureContext,
// etc.) in arch/wasm/stubs.cpp directly.
#include "seh-unwind.cpp"
#endif
