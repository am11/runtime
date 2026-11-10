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

    throw SpmiException(exceptionCode, buffer);
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

    throw SpmiException(EXCEPTIONCODE_RECORDED_EXCEPTION, nullptr);
}

SpmiException::SpmiException(DWORD code, char* message)
    : exCode(code), exMessage(message)
{
}

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
    else if (exCode != 0)
    {
        LogError("Unexpected exception %x was thrown.", exCode);
    }
    else
    {
        LogError("Unexpected exception was thrown.");
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

SpmiException GetCurrentSpmiException()
{
    try
    {
        throw;
    }
    catch (SpmiException& e)
    {
        return e;
    }
    catch (...)
    {
        return SpmiException(0, nullptr);
    }
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

// Catches only non-SuperPMI exceptions. This allows for SuperPMI-thrown exceptions to pass through the JIT
// and be caught by the outer SuperPMI handler.
bool RunWithErrorTrap(void (*function)(void*), void* param)
{
    try
    {
        function(param);
    }
    catch (...)
    {
        if (IsSuperPMIException(GetCurrentSpmiException().GetCode()))
        {
            throw;
        }
        return false;
    }

    return true;
}

// Catches only SuperPMI exceptions. This allows for SuperPMI-thrown exceptions to be caught by the JIT
// and not be caught by the outer SuperPMI handler.
bool RunWithSPMIErrorTrap(void (*function)(void*), void* param)
{
    try
    {
        function(param);
    }
    catch (SpmiException& e)
    {
        if (!IsSuperPMIException(e.GetCode()))
        {
            throw;
        }
        return false;
    }

    return true;
}
