// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "common.h"
#include "do_vxsort.h"

#include <minipal/cpufeatures.h>

static bool s_initialized;
static int s_supportedISA;

bool IsSupportedInstructionSet (InstructionSet instructionSet)
{
    assert(s_initialized);
    assert(instructionSet == InstructionSet::AVX2 || instructionSet == InstructionSet::AVX512F);
    return ((int)s_supportedISA & (1 << (int)instructionSet)) != 0;
}

void InitSupportedInstructionSet (int32_t configSetting)
{
    int cpuFeatures = minipal_getcpufeatures();
    int determinedISA = 0;

    if ((cpuFeatures & XArchIntrinsicConstants_Avx2) != 0)
    {
        if ((cpuFeatures & XArchIntrinsicConstants_Avx512) != 0)
            determinedISA = (1 << (int)InstructionSet::AVX2) | (1 << (int)InstructionSet::AVX512F);
        else
            determinedISA = (1 << (int)InstructionSet::AVX2);
    }

    s_supportedISA = determinedISA & configSetting;

    // If AVX2 is disabled, disable AVX512F as well
    if (!(s_supportedISA & (1 << (int)InstructionSet::AVX2)))
        s_supportedISA = 0;

    s_initialized = true;
}
