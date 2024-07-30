// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace System
{
    public static partial class Environment
    {
        public static long WorkingSet
        {
            get
            {
                int size = sizeof(psinfo);
                Debug.Assert(size <= 1024, "psinfo struct size exceeds 1024 bytes.");
                Span<byte> buffer = stackalloc byte[size];

                ref psinfo psi = ref MemoryMarshal.AsRef<psinfo>(buffer);
                if (Interop.procfs.TryReadRawPSInfo(ProcessId, buffer))
                {
                    return (long)psi.pr_rssize * 1024;
                }
                else
                {
                    return 0;
                }
            }
        }
    }
}
