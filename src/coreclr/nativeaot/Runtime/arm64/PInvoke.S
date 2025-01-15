// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


#include <unixasmmacros.inc>
#include "AsmOffsets.inc"

.global RhpTrapThreads
.global TLSCheckInitialized
.global IsTLSNative

//
// TLS Support Cache
//
.data
TLSCheckInitialized: .word 0  // 0 = not initialized, 1 = initialized
IsTLSNative:         .word 0  // 0 = emulated, 1 = native

//
// RhpPInvoke
//
// IN:  X0: address of pinvoke frame
//
// This helper assumes that its callsite is as good to start the stackwalk as the actual PInvoke callsite.
// The codegenerator must treat the callsite of this helper as GC triggering and generate the GC info for it.
// Also, the codegenerator must ensure that there are no live GC references in callee saved registers.
//

NESTED_ENTRY RhpPInvoke, _TEXT, NoHandler
        str     fp, [x0, #OFFSETOF__PInvokeTransitionFrame__m_FramePointer]
        str     lr, [x0, #OFFSETOF__PInvokeTransitionFrame__m_RIP]
        mov     x9, SP
        str     x9, [x0, #OFFSETOF__PInvokeTransitionFrame__m_PreservedRegs]
        mov     x9, #PTFF_SAVE_SP
        str     x9, [x0, #OFFSETOF__PInvokeTransitionFrame__m_Flags]

        // Check if TLS support determination has been done
        ldr     w9, TLSCheckInitialized
        cbnz    w9, 1f  // If initialized, skip to cached handling

        // Determine if native TLS is supported
        bl      CheckTLSNative  // Call runtime helper to check TLS type
        mov     w9, #1          // Mark TLS check as initialized
        str     w9, TLSCheckInitialized
1:
        // Load cached TLS type
        ldr     w9, IsTLSNative
        cbz     w9, UseEmulatedTLS  // If 0, use emulated TLS

        // Native TLS path
        INLINE_GETTHREAD x1
        b       Continue

UseEmulatedTLS:
        GETTHREAD_ETLS_1  // Use emulated TLS logic

Continue:
        str     x1, [x0, #OFFSETOF__PInvokeTransitionFrame__m_pThread]
        str     x0, [x1, #OFFSETOF__Thread__m_pTransitionFrame]
        ret
NESTED_END RhpPInvoke, _TEXT


//
// RhpPInvokeReturn
//
LEAF_ENTRY RhpPInvokeReturn, _TEXT
        ldr         x9, [x0, #OFFSETOF__PInvokeTransitionFrame__m_pThread]
        mov         x10, 0
        str         x10, [x9, #OFFSETOF__Thread__m_pTransitionFrame]

        PREPARE_EXTERNAL_VAR_INDIRECT_W RhpTrapThreads, 9

        cbnz    w9, 0f  // TrapThreadsFlags_None = 0
        ret
0:
        // Passing transition frame pointer in x0
        b         C_FUNC(RhpWaitForGC2)
LEAF_END RhpPInvokeReturn, _TEXT


//
// CheckTLSNative
// Determines if native TLS is supported at runtime.
//
LEAF_ENTRY CheckTLSNative, _TEXT
        // Platform-specific check for native TLS support
        // Example: Check a system feature, hardware flag, or OS capability

        // Assume result of check in w0:
        // w0 = 1 -> native TLS supported
        // w0 = 0 -> use emulated TLS

        cbz     w0, 0f          // If not supported, fall through

        // Cache result as native
        mov     w9, #1
        str     w9, IsTLSNative
        ret

0:      // Cache result as emulated
        mov     w9, #0
        str     w9, IsTLSNative
        ret
LEAF_END CheckTLSNative, _TEXT
