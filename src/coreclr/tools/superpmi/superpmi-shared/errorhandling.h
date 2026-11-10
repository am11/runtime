// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//----------------------------------------------------------
// ErrorHandling.h - Helpers & whatnot for using exceptions for errors
//----------------------------------------------------------
#ifndef _ErrorHandling
#define _ErrorHandling

#include "logging.h"

// EXCEPTIONCODE_DebugBreakorAV is just the base exception number; calls to DebugBreakorAV()
// pass a unique number to add to this. EXCEPTIONCODE_DebugBreakorAV_MAX is the maximum number
// of this exception range.
#define EXCEPTIONCODE_DebugBreakorAV 0xe0421000
#define EXCEPTIONCODE_DebugBreakorAV_MAX 0xe0422000

#define EXCEPTIONCODE_MC 0xe0422000
#define EXCEPTIONCODE_LWM 0xe0423000
#define EXCEPTIONCODE_CALLUTILS 0xe0426000
#define EXCEPTIONCODE_TYPEUTILS 0xe0427000
// Special exception code for rethrowing a recorded exception. This is NOT
// considered an SPMI exception (see IsSuperPMIException), but it also does not
// result in a replay failure (see JitInstance::CompileMethod).
#define EXCEPTIONCODE_RECORDED_EXCEPTION 0xe0428000
#define EXCEPTIONCODE_ASSERT 0xe0440000

// Throw SpmiException
void MSC_ONLY(__declspec(noreturn)) ThrowSpmiException(DWORD exceptionCode, const char* message, ...);
void MSC_ONLY(__declspec(noreturn)) ThrowRecordedException(DWORD innerExceptionCode);

// Assert stuff
#define AssertCodeMsg(expr, exCode, msg, ...)                                                                          \
    do                                                                                                                 \
    {                                                                                                                  \
        if (!(expr))                                                                                                   \
            LogException(exCode, "SuperPMI assertion '%s' failed (" msg ")", #expr, ##__VA_ARGS__);                    \
    } while (0)

#define AssertCode(expr, exCode)                                                                                       \
    do                                                                                                                 \
    {                                                                                                                  \
        if (!(expr))                                                                                                   \
            LogException(exCode, "SuperPMI assertion '%s' failed", #expr);                                             \
    } while (0)

#define AssertMapExists(map, keymsg, ...)                                                                              \
    do                                                                                                                 \
    {                                                                                                                  \
        if (map == nullptr)                                                                                            \
            LogException(EXCEPTIONCODE_MC, "SuperPMI assertion failed (missing map " #map ")" keymsg, ##__VA_ARGS__);  \
    } while (0)

#define LookupByKeyOrMiss(map, key, keymsg, ...)                                                                                        \
    [&]() {                                                                                                                             \
      if (map == nullptr)                                                                                                               \
          LogException(EXCEPTIONCODE_MC, "SuperPMI assertion failed (missing map " #map ")" keymsg, ##__VA_ARGS__);                     \
      int index = map->GetIndex(key);                                                                                                   \
      if (index == -1)                                                                                                                  \
          LogException(EXCEPTIONCODE_MC, "SuperPMI assertion failed (missing key \"" #key "\" in map " #map ")" keymsg, ##__VA_ARGS__); \
      return map->GetItem(index);                                                                                                       \
    }()

// clang doesn't allow for an empty __VA_ARGS__, so we need to pass something non-empty to `LogException`, below.

#define AssertMapExistsNoMessage(map)                                                                                  \
    do                                                                                                                 \
    {                                                                                                                  \
        if (map == nullptr)                                                                                            \
            LogException(EXCEPTIONCODE_MC, "SuperPMI assertion failed (missing map " #map ")", "");                    \
    } while (0)

#define LookupByKeyOrMissNoMessage(map, key)                                                                          \
    [&]() {                                                                                                           \
      if (map == nullptr)                                                                                             \
          LogException(EXCEPTIONCODE_MC, "SuperPMI assertion failed (missing map " #map ")", "");                     \
      int index = map->GetIndex(key);                                                                                 \
      if (index == -1)                                                                                                \
          LogException(EXCEPTIONCODE_MC, "SuperPMI assertion failed (missing key \"" #key "\" in map " #map ")", ""); \
      return map->GetItem(index);                                                                                     \
    }()

#define AssertMsg(expr, msg, ...) AssertCodeMsg(expr, EXCEPTIONCODE_ASSERT, msg, ##__VA_ARGS__)
#define Assert(expr) AssertCode(expr, EXCEPTIONCODE_ASSERT)

extern bool IsSuperPMIException(unsigned code);

class SpmiException
{
private:
    DWORD exCode;
    char* exMessage; // 'new' memory allocated by ThrowSpmiException()

public:
    SpmiException(DWORD code, char* message);

    char* GetExceptionMessage();
    DWORD GetCode();

    void ShowAndDeleteMessage();
    void DeleteMessage();
};

// Returns the exception being handled as an SpmiException. Exceptions not thrown by SuperPMI have code 0
// and no message. Must be called from a catch block.
extern SpmiException GetCurrentSpmiException();

extern bool RunWithErrorTrap(void (*function)(void*), void* param);
extern bool RunWithSPMIErrorTrap(void (*function)(void*), void* param);

// Runs 'function' and then 'finally' with the exception code it threw, or 0. Exceptions thrown by the
// runtime cannot be inspected here, so they are recorded as 1; replay only checks for non-zero.
template <typename LambdaTry, typename LambdaFinally>
void RunWithErrorExceptionCodeCaptureAndContinue(LambdaTry function, LambdaFinally finally)
{
    try
    {
        function();
    }
    catch (...)
    {
        DWORD exceptionCode = GetCurrentSpmiException().GetCode();
        finally(exceptionCode != 0 ? exceptionCode : 1);
        throw;
    }
    finally(0);
}

#endif
