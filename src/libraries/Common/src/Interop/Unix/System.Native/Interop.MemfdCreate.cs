// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

internal static partial class Interop
{
    internal static partial class Sys
    {
        [LibraryImport(Libraries.SystemNative, EntryPoint = "SystemNative_MemfdCreate", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        internal static partial SafeFileHandle MemfdCreate(string name);

        [LibraryImport(Libraries.SystemNative, EntryPoint = "SystemNative_MemfdSupported", SetLastError = true)]
        private static partial int MemfdSupportedImpl();

        private static volatile int s_memfdSupported = -1;

        internal static bool MemfdSupported
        {
            get
            {
                int result = Interlocked.CompareExchange(ref s_memfdSupported, -1, -1);
                if (result == -1)
                {
                    result = MemfdSupportedImpl();
                    Interlocked.Exchange(ref s_memfdSupported, result);
                }
                return result == 1;
            }
        }
    }
}
