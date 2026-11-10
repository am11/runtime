// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*============================================================
** CorImage.cpp
**
** IMAGEHLP routines so we can avoid early binding to that DLL.
===========================================================*/

#include "stdafx.h"
#include "contract.h"
#include <daccess.h>
#include "corimage.h"
#include "safemath.h"

#define RTL_MEG                   (1024UL * 1024UL)
#define RTLP_IMAGE_MAX_DOS_HEADER ( 256UL * RTL_MEG)

// IMAGE_FIRST_SECTION doesn't need 32/64 versions since the file header is
// the same either way.

#define PTR_IMAGE_FIRST_SECTION( ntheader )                            \
   PTR_IMAGE_SECTION_HEADER                                            \
    (dac_cast<TADDR>(ntheader) +                                       \
     offsetof( IMAGE_NT_HEADERS, OptionalHeader ) +                \
     VAL16((ntheader)->FileHeader.SizeOfOptionalHeader)                \
    )

#ifndef DACCESS_COMPILE

IMAGE_NT_HEADERS *Cor_RtlImageNtHeader(VOID *pvBase, ULONG FileLength)
{
    LIMITED_METHOD_CONTRACT;
    IMAGE_NT_HEADERS *pNtHeaders = NULL;
    if (pvBase && (pvBase != (VOID*)-1)) {
        IMAGE_DOS_HEADER *pDos = (IMAGE_DOS_HEADER*)pvBase;

#ifdef HOST_WINDOWS
        __try
#else
        try
#endif
        {
            DEBUG_OK_TO_THROW_BEGIN
            if (   (pDos->e_magic == VAL16(IMAGE_DOS_SIGNATURE))
                && ((DWORD)VAL32(pDos->e_lfanew) < RTLP_IMAGE_MAX_DOS_HEADER)
                && ovadd_lt((DWORD)VAL32(pDos->e_lfanew), sizeof(IMAGE_FILE_HEADER) + sizeof(DWORD), FileLength)) {
                pNtHeaders = (IMAGE_NT_HEADERS*)((BYTE*)pDos + VAL32(pDos->e_lfanew));
                if (pNtHeaders->Signature != VAL32(IMAGE_NT_SIGNATURE))
                    pNtHeaders = NULL;
            }
            DEBUG_OK_TO_THROW_END
        }
#ifdef HOST_WINDOWS
        __except (EXCEPTION_EXECUTE_HANDLER)
#else
        catch (PAL_SEHException&)
#endif
        {
            pNtHeaders = NULL;
        }
    }
    return pNtHeaders;
}

#endif // #ifndef DACCESS_COMPILE

EXTERN_C PIMAGE_SECTION_HEADER
Cor_RtlImageRvaToSection32(PTR_IMAGE_NT_HEADERS32 NtHeaders,
                           ULONG Rva,
                           ULONG FileLength)
{
    LIMITED_METHOD_CONTRACT;
    ULONG i;
    PTR_IMAGE_SECTION_HEADER NtSection;

    NtSection = PTR_IMAGE_FIRST_SECTION( NtHeaders );
    for (i=0; i<NtHeaders->FileHeader.NumberOfSections; i++) {
        if (FileLength &&
            (((VAL32(NtSection->PointerToRawData) > FileLength)) ||
            (VAL32(NtSection->SizeOfRawData) > FileLength - VAL32(NtSection->PointerToRawData))))
            return NULL;
        if (Rva >= VAL32(NtSection->VirtualAddress) &&
            Rva < VAL32(NtSection->VirtualAddress) + VAL32(NtSection->SizeOfRawData))
            return NtSection;

        ++NtSection;
    }

    return NULL;
}

EXTERN_C PIMAGE_SECTION_HEADER
Cor_RtlImageRvaToSection64(PTR_IMAGE_NT_HEADERS64 NtHeaders,
                           ULONG Rva,
                           ULONG FileLength)
{
    LIMITED_METHOD_CONTRACT;
    ULONG i;
    PTR_IMAGE_SECTION_HEADER NtSection;

    NtSection = PTR_IMAGE_FIRST_SECTION( NtHeaders );
    for (i=0; i<VAL16(NtHeaders->FileHeader.NumberOfSections); i++) {
        if (FileLength &&
            (((VAL32(NtSection->PointerToRawData) > FileLength)) ||
            (VAL32(NtSection->SizeOfRawData) > FileLength - VAL32(NtSection->PointerToRawData))))
            return NULL;
        if (Rva >= VAL32(NtSection->VirtualAddress) &&
            Rva < VAL32(NtSection->VirtualAddress) + VAL32(NtSection->SizeOfRawData))
            return NtSection;

        ++NtSection;
    }

    return NULL;
}

EXTERN_C PBYTE Cor_RtlImageRvaToVa32(PTR_IMAGE_NT_HEADERS32 NtHeaders,
                                     PBYTE Base,
                                     ULONG Rva,
                                     ULONG FileLength)
{
    LIMITED_METHOD_CONTRACT;
    PIMAGE_SECTION_HEADER NtSection =
        Cor_RtlImageRvaToSection32(NtHeaders,
                                   Rva,
                                   FileLength);

    if (NtSection != NULL)
        return (Base +
                (Rva - VAL32(NtSection->VirtualAddress)) +
                VAL32(NtSection->PointerToRawData));
    else
        return NULL;
}

EXTERN_C PBYTE Cor_RtlImageRvaToVa64(PTR_IMAGE_NT_HEADERS64 NtHeaders,
                                     PBYTE Base,
                                     ULONG Rva,
                                     ULONG FileLength)
{
    LIMITED_METHOD_CONTRACT;
    PIMAGE_SECTION_HEADER NtSection =
        Cor_RtlImageRvaToSection64(NtHeaders,
                                   Rva,
                                   FileLength);

    if (NtSection != NULL)
        return (Base +
                (Rva - VAL32(NtSection->VirtualAddress)) +
                VAL32(NtSection->PointerToRawData));
    else
        return NULL;
}
