// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// stdafx.h
//

//
// Common include file for utility code.
//*****************************************************************************
#pragma once

#include <switches.h>
#include <crtwrap.h>
#include <dn-u16.h>
#include <algorithm>
using std::min;
using std::max;

#define IN_WINFIX_CPP

#include <winwrap.h>

#include "volatile.h"
#include "static_assert.h"

#ifdef TARGET_X86
#ifdef TARGET_WINDOWS
#define NAKED_ATTRIBUTE __declspec(naked)
#else
#define NAKED_ATTRIBUTE __attribute__((naked))
#endif
#endif
