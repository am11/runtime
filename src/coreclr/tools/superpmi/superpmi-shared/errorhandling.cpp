// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "standardpch.h"
#include "errorhandling.h"
#include "logging.h"
#include "runtimedetails.h"
#include "spmiutil.h"

// Allocating memory here seems moderately dangerous: we'll probably leak like a sieve...
void MSC_ONLY(__declspec(noreturn)) ThrowSpmiException(DWORD exceptionCode, va_list args, const char* message)
{
    assert(IsSuperPMIException(exceptionCode));

    char*      buffer = new char[8192];
    _vsnprintf_s(buffer, 8192, 8191, message, args);

    if (BreakOnException())
        DEBUG_BREAK;

    ULONG_PTR exArgs[1];
    exArgs[0] = (ULONG_PTR)buffer;
    RAISE_SEH_EXCEPTION(exceptionCode, 0, ArrLen(exArgs), exArgs);
}

void MSC_ONLY(__declspec(noreturn)) ThrowSpmiException(DWORD exceptionCode, const char* msg, ...)
{
    va_list ap;
    va_start(ap, msg);
    ThrowSpmiException(exceptionCode, ap, msg);
}

// Throw an exception that indicates that the EE side threw an exception during recording.
// These exceptions do not result in replay errors; see JitInstance::CompileMethod.
// Note that we rely on the fact that the JIT do not look closely at exceptions thrown by the EE;
// otherwise this could cause issues since it changes the exception code from what was actually
// thrown.
//
// However, do note that crossgen2/ilc and the VM already do not use the same exception code when
// throwing exceptions.
//
void MSC_ONLY(__declspec(noreturn)) ThrowRecordedException(DWORD innerExceptionCode)
{
    if (BreakOnException())
        DEBUG_BREAK;

    ULONG_PTR args[1];
    args[0] = (ULONG_PTR)innerExceptionCode;
    RAISE_SEH_EXCEPTION(EXCEPTIONCODE_RECORDED_EXCEPTION, 0, ArrLen(args), args);
}

SpmiException::SpmiException(FilterSuperPMIExceptionsParam_CaptureException* e)
    : exCode(e->exceptionCode), exMessage(e->exceptionMessage)
{
}

#if 0
SpmiException::~SpmiException()
{
    delete[] exMessage;
    exMessage = nullptr;
}
#endif

char* SpmiException::GetExceptionMessage()
{
    return exMessage;
}

void SpmiException::ShowAndDeleteMessage()
{
    if (exMessage != nullptr)
    {
        LogError("Exception thrown: %s", exMessage);
    }
    else
    {
        LogError("Unexpected exception %x was thrown.", exCode);
    }

    DeleteMessage();
}

void SpmiException::DeleteMessage()
{
    delete[] exMessage;
    exMessage = nullptr;
}

DWORD SpmiException::GetCode()
{
    return exCode;
}

// This filter function executes the handler only for EXCEPTIONCODE_MC, otherwise it continues the handler search.
LONG FilterSuperPMIExceptions_CatchMC(PEXCEPTION_POINTERS pExceptionPointers, LPVOID lpvParam)
{
    return (pExceptionPointers->ExceptionRecord->ExceptionCode == EXCEPTIONCODE_MC) ? EXCEPTION_EXECUTE_HANDLER
                                                                                    : EXCEPTION_CONTINUE_SEARCH;
}

// This filter function captures the exception pointers and continues searching.
LONG FilterSuperPMIExceptions_CaptureExceptionAndContinue(PEXCEPTION_POINTERS pExceptionPointers, LPVOID lpvParam)
{
    FilterSuperPMIExceptionsParam_CaptureException* pSPMIEParam = (FilterSuperPMIExceptionsParam_CaptureException*)lpvParam;
    pSPMIEParam->Initialize(pExceptionPointers);
    return EXCEPTION_CONTINUE_SEARCH;
}

LONG FilterSuperPMIExceptions_CaptureExceptionAndStop(PEXCEPTION_POINTERS pExceptionPointers, LPVOID lpvParam)
{
    FilterSuperPMIExceptionsParam_CaptureException* pSPMIEParam = (FilterSuperPMIExceptionsParam_CaptureException*)lpvParam;
    pSPMIEParam->Initialize(pExceptionPointers);
    return EXCEPTION_EXECUTE_HANDLER;
}

bool IsSuperPMIException(unsigned code)
{
    switch (code)
    {
        case EXCEPTIONCODE_MC:
        case EXCEPTIONCODE_LWM:
        case EXCEPTIONCODE_CALLUTILS:
        case EXCEPTIONCODE_TYPEUTILS:
        case EXCEPTIONCODE_ASSERT:
            return true;
        default:
            if ((EXCEPTIONCODE_DebugBreakorAV <= code) && (code < EXCEPTIONCODE_DebugBreakorAV_MAX))
            {
                return true;
            }
            return false;
    }
}

// This filter function executes the handler only for non-SuperPMI generated exceptions, otherwise it continues the
// handler search. This allows for SuperPMI-thrown exceptions to pass through the JIT and be caught by the outer
// SuperPMI handler.
LONG FilterSuperPMIExceptions_CatchNonSuperPMIException(PEXCEPTION_POINTERS pExceptionPointers, LPVOID lpvParam)
{
    return !IsSuperPMIException(pExceptionPointers->ExceptionRecord->ExceptionCode);
}


// This filter function executes the handler only for SuperPMI generated exceptions, otherwise it continues the
// handler search. This allows for SuperPMI-thrown exceptions to be caught by the JIT and not be caught by the outer
// SuperPMI handler.
LONG FilterSuperPMIExceptions_CatchSuperPMIException(PEXCEPTION_POINTERS pExceptionPointers, LPVOID lpvParam)
{
    return IsSuperPMIException(pExceptionPointers->ExceptionRecord->ExceptionCode);
}


bool RunWithErrorTrap(void (*function)(void*), void* param)
{
    bool success = true;

#ifdef HOST_WINDOWS
    __try
#else
    try
#endif
    {
        HardwareExceptionHolder
        DEBUG_OK_TO_THROW_BEGIN
        function(param);
        DEBUG_OK_TO_THROW_END
    }
#ifdef HOST_WINDOWS
    __except (FilterSuperPMIExceptions_CatchNonSuperPMIException(GetExceptionInformation(), NULL))
#else
    catch (PAL_SEHException& ex)
#endif
    {
#ifndef HOST_WINDOWS
        if (FilterSuperPMIExceptions_CatchNonSuperPMIException(&ex.ExceptionPointers, NULL) == EXCEPTION_CONTINUE_SEARCH)
        {
            throw;
        }
#endif
        success = false;
    }

    return success;
}

bool RunWithSPMIErrorTrap(void (*function)(void*), void* param)
{
    bool success = true;

#ifdef HOST_WINDOWS
    __try
#else
    try
#endif
    {
        HardwareExceptionHolder
        DEBUG_OK_TO_THROW_BEGIN
        function(param);
        DEBUG_OK_TO_THROW_END
    }
#ifdef HOST_WINDOWS
    __except (FilterSuperPMIExceptions_CatchSuperPMIException(GetExceptionInformation(), NULL))
#else
    catch (PAL_SEHException& ex)
#endif
    {
#ifndef HOST_WINDOWS
        if (FilterSuperPMIExceptions_CatchSuperPMIException(&ex.ExceptionPointers, NULL) == EXCEPTION_CONTINUE_SEARCH)
        {
            throw;
        }
#endif
        success = false;
    }

    return success;
}

void RunWithErrorExceptionCodeCaptureAndContinueImp(void* param, void (*function)(void*), void (*finallyFunction)(void*, DWORD))
{
    FilterSuperPMIExceptionsParam_CaptureException paramStruct;

#ifdef HOST_UNIX
    // We can't capture the exception code as a PAL exceptions when the exception is
    // thrown from crossgen2 on Linux as the jitinterface dll does not use the PAL. So
    // assume there will be some error, then set it to zero (no error)
    // if the called function doesn't throw.
    //
    // While not quite matching the behavior on Windows, for C++ exceptions this will
    // be equivalent. (All C++ exceptions on Windows have the same exception code.)
    paramStruct.exceptionCode = 1;
#endif // HOST_UNIX

#ifdef HOST_WINDOWS
    __try
#else
    try
#endif
    {
        HardwareExceptionHolder
        DEBUG_OK_TO_THROW_BEGIN
#ifdef HOST_WINDOWS
        __try
#else
        try
#endif
        {
            HardwareExceptionHolder
            DEBUG_OK_TO_THROW_BEGIN
            function(param);
#ifdef HOST_UNIX
            paramStruct.exceptionCode = 0;
#endif // HOST_UNIX
            DEBUG_OK_TO_THROW_END
        }
#ifdef HOST_WINDOWS
        __except (FilterSuperPMIExceptions_CaptureExceptionAndContinue(GetExceptionInformation(), &paramStruct))
#else
        catch (PAL_SEHException& ex)
#endif
        {
#ifndef HOST_WINDOWS
            if (FilterSuperPMIExceptions_CaptureExceptionAndContinue(&ex.ExceptionPointers, &paramStruct) == EXCEPTION_CONTINUE_SEARCH)
            {
                throw;
            }
#endif
        }
        DEBUG_OK_TO_THROW_END
    }
#ifdef HOST_WINDOWS
    __finally
    {
        finallyFunction(param, paramStruct.exceptionCode);
    }
#else
    catch (...)
    {
        finallyFunction(param, paramStruct.exceptionCode);
        throw;
    }
    finallyFunction(param, paramStruct.exceptionCode);
#endif
}
