// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ===========================================================================
// File: palclr.h
//
// Various macros and constants that are necessary to make the CLR portable.
// ===========================================================================

#ifndef __PALCLR_H__
#define __PALCLR_H__

#include <minipal/utils.h>

#if defined(HOST_WINDOWS)

// This macro is used to standardize the wide character string literals between UNIX and Windows.
// Unix L"" is UTF32, and on windows it's UTF16.  Because of built-in assumptions on the size
// of string literals, it's important to match behaviour between Unix and Windows.  Unix will be defined
// as u"" (char16_t)
#define W(str)  L##str

#include <windef.h>

#if !defined(_DEBUG_IMPL) && defined(_DEBUG) && !defined(DACCESS_COMPILE)
#define _DEBUG_IMPL 1
#endif

#if __GNUC__
#ifndef __cdecl
#define __cdecl	__attribute__((__cdecl__))
#endif
#endif

#ifndef NOTHROW_DECL
#ifdef _MSC_VER
#define NOTHROW_DECL __declspec(nothrow)
#else
#define NOTHROW_DECL __attribute__((nothrow))
#endif // !_MSC_VER
#endif // !NOTHROW_DECL

// PORTABILITY_ASSERT and PORTABILITY_WARNING macros are meant to be used to
// mark places in the code that needs attention for portability. The usual
// usage pattern is:
//
// int get_scratch_register() {
// #if defined(TARGET_X86)
//     return eax;
// #elif defined(TARGET_AMD64)
//     return rax;
// #elif defined(TARGET_ARM)
//     return r0;
// #else
//     PORTABILITY_ASSERT("scratch register");
//     return 0;
// #endif
// }
//
// PORTABILITY_ASSERT is meant to be used inside functions/methods. It can
// introduce compile-time and/or run-time errors.
// PORTABILITY_WARNING is meant to be used outside functions/methods. It can
// introduce compile-time errors or warnings only.
//
// People starting new ports will first define these to just cause run-time
// errors. Once they fix all the places that need attention for portability,
// they can define PORTABILITY_ASSERT and PORTABILITY_WARNING to cause
// compile-time errors to make sure that they haven't missed anything.
//
// If it is reasonably possible all codepaths containing PORTABILITY_ASSERT
// should be compilable (e.g. functions should return NULL or something if
// they are expected to return a value).
//
// The message in these two macros should not contain any keywords like TODO
// or NYI. It should be just the brief description of the problem.

#if defined(TARGET_X86) || defined(TARGET_AMD64) || defined(TARGET_ARM) || defined(TARGET_ARM64)
// Finished ports - compile-time errors
#define PORTABILITY_WARNING(message)    NEED_TO_PORT_THIS_ONE(NEED_TO_PORT_THIS_ONE)
#define PORTABILITY_ASSERT(message)     NEED_TO_PORT_THIS_ONE(NEED_TO_PORT_THIS_ONE)
#else
// Ports in progress - run-time asserts only
#define PORTABILITY_WARNING(message)
#define PORTABILITY_ASSERT(message)     _ASSERTE(false && (message))
#endif // TARGET_X86 || TARGET_AMD64 || TARGET_ARM || TARGET_ARM64

#define DIRECTORY_SEPARATOR_CHAR_A '\\'
#define DIRECTORY_SEPARATOR_STR_A "\\"
#define DIRECTORY_SEPARATOR_CHAR_W W('\\')
#define DIRECTORY_SEPARATOR_STR_W W("\\")

#define PATH_SEPARATOR_CHAR_W W(';')
#define PATH_SEPARATOR_STR_W W(";")

#define VOLUME_SEPARATOR_CHAR_W W(':')

// PAL Macros
// Not all compilers support fully anonymous aggregate types, so the
// PAL provides names for those types. To allow existing definitions of
// those types to continue to work, we provide macros that should be
// used to reference fields within those types.

#ifndef DECIMAL_SCALE
#define DECIMAL_SCALE(dec)       ((dec).scale)
#endif

#ifndef DECIMAL_SIGN
#define DECIMAL_SIGN(dec)        ((dec).sign)
#endif

#ifndef DECIMAL_SIGNSCALE
#define DECIMAL_SIGNSCALE(dec)   ((dec).signscale)
#endif

#ifndef DECIMAL_LO32
#define DECIMAL_LO32(dec)        ((dec).Lo32)
#endif

#ifndef DECIMAL_MID32
#define DECIMAL_MID32(dec)       ((dec).Mid32)
#endif

#ifndef DECIMAL_HI32
#define DECIMAL_HI32(dec)       ((dec).Hi32)
#endif

#ifndef DECIMAL_LO64_GET
#define DECIMAL_LO64_GET(dec)       ((dec).Lo64)
#endif

#ifndef DECIMAL_LO64_SET
#define DECIMAL_LO64_SET(dec,value)   {(dec).Lo64 = value; }
#endif

#ifndef IMAGE_RELOC_FIELD
#define IMAGE_RELOC_FIELD(img, f)      ((img).f)
#endif

#ifndef IMAGE_IMPORT_DESC_FIELD
#define IMAGE_IMPORT_DESC_FIELD(img, f)     ((img).f)
#endif

#define IMAGE_RDE_ID(img) ((img)->Id)

#define IMAGE_RDE_NAME(img) ((img)->Name)

#define IMAGE_RDE_OFFSET(img) ((img)->OffsetToData)

#ifndef IMAGE_RDE_NAME_FIELD
#define IMAGE_RDE_NAME_FIELD(img, f)    ((img)->f)
#endif

#define IMAGE_RDE_OFFSET_FIELD(img, f) ((img)->f)

#ifndef IMAGE_FE64_FIELD
#define IMAGE_FE64_FIELD(img, f)    ((img).f)
#endif

#ifndef IMPORT_OBJ_HEADER_FIELD
#define IMPORT_OBJ_HEADER_FIELD(obj, f)    ((obj).f)
#endif

#ifndef IMAGE_COR20_HEADER_FIELD
#define IMAGE_COR20_HEADER_FIELD(obj, f)    ((obj).f)
#endif

#include "staticcontract.h"

#define PAL_CPP_TRY try
#define PAL_CPP_ENDTRY
#define PAL_CPP_THROW(type, obj) do { throw obj; } while (false)
#define PAL_CPP_RETHROW do { throw; } while (false)
#define PAL_CPP_CATCH_DERIVED(type, obj) catch (type * obj)
#define PAL_CPP_CATCH_NON_DERIVED(type, obj) catch (type obj)
#define PAL_CPP_CATCH_NON_DERIVED_NOARG(type) catch (type)
#define PAL_CPP_CATCH_ALL catch (...)


#if !BIGENDIAN
// For little-endian machines, do nothing
#define VAL16(x) x
#define VAL32(x) x
#define VAL64(x) x
#define SwapString(x)
#define SwapStringLength(x, y)
#define SwapGuid(x)
#endif  // !BIGENDIAN

#ifdef _MSC_VER
// Get Unaligned values from a potentially unaligned object
#define GET_UNALIGNED_16(_pObject)  (*(UINT16 UNALIGNED *)(_pObject))
#define GET_UNALIGNED_32(_pObject)  (*(UINT32 UNALIGNED *)(_pObject))
#define GET_UNALIGNED_64(_pObject)  (*(UINT64 UNALIGNED *)(_pObject))

// Set Value on an potentially unaligned object
#define SET_UNALIGNED_16(_pObject, _Value)  (*(UNALIGNED UINT16 *)(_pObject)) = (UINT16)(_Value)
#define SET_UNALIGNED_32(_pObject, _Value)  (*(UNALIGNED UINT32 *)(_pObject)) = (UINT32)(_Value)
#define SET_UNALIGNED_64(_pObject, _Value)  (*(UNALIGNED UINT64 *)(_pObject)) = (UINT64)(_Value)

// Get Unaligned values from a potentially unaligned object and swap the value
#define GET_UNALIGNED_VAL16(_pObject) VAL16(GET_UNALIGNED_16(_pObject))
#define GET_UNALIGNED_VAL32(_pObject) VAL32(GET_UNALIGNED_32(_pObject))
#define GET_UNALIGNED_VAL64(_pObject) VAL64(GET_UNALIGNED_64(_pObject))

// Set a swap Value on an potentially unaligned object
#define SET_UNALIGNED_VAL16(_pObject, _Value) SET_UNALIGNED_16(_pObject, VAL16((UINT16)_Value))
#define SET_UNALIGNED_VAL32(_pObject, _Value) SET_UNALIGNED_32(_pObject, VAL32((UINT32)_Value))
#define SET_UNALIGNED_VAL64(_pObject, _Value) SET_UNALIGNED_64(_pObject, VAL64((UINT64)_Value))
#endif

#ifdef HOST_64BIT
#define VALPTR(x) VAL64(x)
#define GET_UNALIGNED_PTR(x) GET_UNALIGNED_64(x)
#define GET_UNALIGNED_VALPTR(x) GET_UNALIGNED_VAL64(x)
#define SET_UNALIGNED_PTR(p,x) SET_UNALIGNED_64(p,x)
#define SET_UNALIGNED_VALPTR(p,x) SET_UNALIGNED_VAL64(p,x)
#else
#define VALPTR(x) VAL32(x)
#define GET_UNALIGNED_PTR(x) GET_UNALIGNED_32(x)
#define GET_UNALIGNED_VALPTR(x) GET_UNALIGNED_VAL32(x)
#define SET_UNALIGNED_PTR(p,x) SET_UNALIGNED_32(p,x)
#define SET_UNALIGNED_VALPTR(p,x) SET_UNALIGNED_VAL32(p,x)
#endif

#define MAKEDLLNAME_W(name) name W(".dll")
#define MAKEDLLNAME_A(name) name  ".dll"

#ifdef UNICODE
#define MAKEDLLNAME(x) MAKEDLLNAME_W(x)
#else
#define MAKEDLLNAME(x) MAKEDLLNAME_A(x)
#endif

#if !defined(MAX_LONGPATH)
#define MAX_LONGPATH   260 /* max. length of full pathname */
#endif
#if !defined(MAX_PATH_FNAME)
#define MAX_PATH_FNAME   MAX_PATH /* max. length of full pathname */
#endif

#define __clr_reserved __reserved

// Native system libray handle.
// In Windows, NATIVE_LIBRARY_HANDLE is the same as HMODULE.
typedef HMODULE NATIVE_LIBRARY_HANDLE;

#ifndef IMAGE_FILE_MACHINE_LOONGARCH64
#define IMAGE_FILE_MACHINE_LOONGARCH64       0x6264  // LOONGARCH64.
#endif

#ifndef IMAGE_FILE_MACHINE_RISCV64
#define IMAGE_FILE_MACHINE_RISCV64       0x5064  // RISCV64.
#endif

#endif // defined(HOST_WINDOWS)

// Contract bookkeeping for the body of a __try that catches its own exceptions:
// the body is allowed to throw even if the enclosing function is NOTHROW.
#if defined(HOST_WINDOWS) && defined(_DEBUG_IMPL) && !defined(JIT_BUILD) && !defined(CROSS_COMPILE) && !defined(DISABLE_CONTRACTS)
#define DEBUG_OK_TO_THROW_BEGIN                                                 \
    BOOL ___oldOkayToThrowValue = FALSE;                                        \
    ClrDebugState *___pState = ::GetClrDebugState();                            \
    __try                                                                       \
    {                                                                           \
        ___oldOkayToThrowValue = ___pState->IsOkToThrow();                      \
        ___pState->SetOkToThrow();

// Special version that avoids touching the debug state after doing work in a DllMain for process or thread detach.
#define DEBUG_OK_TO_THROW_BEGIN_DLLMAIN(_reason)                                \
    BOOL ___oldOkayToThrowValue = FALSE;                                        \
    ClrDebugState *___pState = NULL;                                            \
    if (_reason != DLL_PROCESS_ATTACH)                                          \
        ___pState = CheckClrDebugState();                                       \
    __try                                                                       \
    {                                                                           \
        if (___pState)                                                          \
        {                                                                       \
            ___oldOkayToThrowValue = ___pState->IsOkToThrow();                  \
            ___pState->SetOkToThrow();                                          \
        }                                                                       \
        if ((_reason == DLL_PROCESS_DETACH) || (_reason == DLL_THREAD_DETACH))  \
        {                                                                       \
            ___pState = NULL;                                                   \
        }

#define DEBUG_OK_TO_THROW_END                                                   \
    }                                                                           \
    __finally                                                                   \
    {                                                                           \
        if (___pState != NULL)                                                  \
        {                                                                       \
            _ASSERTE(___pState == CheckClrDebugState());                        \
            ___pState->SetOkToThrow( ___oldOkayToThrowValue );                  \
        }                                                                       \
    }
#else
#define DEBUG_OK_TO_THROW_BEGIN
#define DEBUG_OK_TO_THROW_BEGIN_DLLMAIN(_reason)
#define DEBUG_OK_TO_THROW_END
#endif

// Raise a Win32 SEH exception. On Unix, it is thrown as a PAL_SEHException carrying the
// exception record and the context of the raise site.
#ifdef HOST_WINDOWS
#define RAISE_SEH_EXCEPTION(code, flags, numArgs, args) RaiseException(code, flags, numArgs, args)
#else // HOST_WINDOWS

#if defined(HOST_AMD64)
#define SEH_CONTEXT_PC(pContext) ((pContext)->Rip)
#elif defined(HOST_S390X)
#define SEH_CONTEXT_PC(pContext) ((pContext)->PSWAddr)
#elif defined(HOST_POWERPC64)
#define SEH_CONTEXT_PC(pContext) ((pContext)->Nip)
#else
#define SEH_CONTEXT_PC(pContext) ((pContext)->Pc)
#endif

#if defined(HOST_ARM) || defined(HOST_ARM64) || defined(HOST_LOONGARCH64) || defined(HOST_RISCV64)
#define SEH_RAISE_CONTEXT_FLAGS (CONTEXT_FULL | CONTEXT_UNWOUND_TO_CALL)
#else
#define SEH_RAISE_CONTEXT_FLAGS CONTEXT_FULL
#endif

// RtlCaptureContext reads the requested ContextFlags on some architectures and adds
// CONTEXT_SEGMENTS / CONTEXT_DEBUG on others, so the flags are set before and after.
// WASM-TODO: reconsider this
#ifdef TARGET_WASM
#define SEH_CAPTURE_CONTEXT(pContext) ((PVOID)NULL)
#else
#define SEH_CAPTURE_CONTEXT(pContext)                                           \
    ((pContext)->ContextFlags = CONTEXT_FULL,                                   \
     RtlCaptureContext(pContext),                                               \
     (pContext)->ContextFlags = SEH_RAISE_CONTEXT_FLAGS,                        \
     (PVOID)SEH_CONTEXT_PC(pContext))
#endif

#define RAISE_SEH_EXCEPTION(code, flags, numArgs, args)                         \
    do                                                                          \
    {                                                                           \
        EXCEPTION_RECORD* __exceptionRecord;                                    \
        CONTEXT* __contextRecord;                                               \
        PAL_AllocateExceptionRecords(&__exceptionRecord, &__contextRecord);     \
        memset(__exceptionRecord, 0, sizeof(EXCEPTION_RECORD));                 \
        memset(__contextRecord, 0, sizeof(CONTEXT));                            \
        __exceptionRecord->ExceptionCode = (code);                              \
        __exceptionRecord->ExceptionFlags = (flags);                            \
        __exceptionRecord->NumberParameters = (numArgs);                        \
        if ((numArgs) != 0)                                                     \
        {                                                                       \
            memcpy(__exceptionRecord->ExceptionInformation, (args),             \
                   (numArgs) * sizeof(ULONG_PTR));                              \
        }                                                                       \
        __exceptionRecord->ExceptionAddress = SEH_CAPTURE_CONTEXT(__contextRecord); \
        throw PAL_SEHException(__exceptionRecord, __contextRecord);             \
    } while (false)

#endif // HOST_WINDOWS

#endif // __PALCLR_H__
