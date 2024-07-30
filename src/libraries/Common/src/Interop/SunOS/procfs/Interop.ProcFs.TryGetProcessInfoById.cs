// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.IO;

internal static partial class Interop
{
    internal static partial class @procfs
    {

        /// <summary>
        /// Attempts to get status info for the specified process ID.
        /// </summary>
        /// <param name="pid">PID of the process to read status info for.</param>
        /// <param name="result">The pointer to ProcessInfo instance.</param>
        /// <returns>
        /// true if the process info was read; otherwise, false.
        /// </returns>

        // ProcessManager.SunOS.cs calls this
        // "unsafe" due to use of fixed-size buffers
        internal static bool TryGetProcessInfoById(int pid, out ProcessInfo result)
        {
            int size = sizeof(psinfo);
            Debug.Assert(size <= 1024, "psinfo struct size exceeds 1024 bytes.");
            Span<byte> buffer = stackalloc byte[size];

            if (!TryReadRawPSInfo(pid, buffer))
            {
                result = default;
                return false;
            }

            ref psinfo psi = ref MemoryMarshal.AsRef<psinfo>(buffer);
            result.Pid = psi.pr_pid;
            result.ParentPid = psi.pr_ppid;
            result.SessionId = psi.pr_sid;
            result.VirtualSize = (nuint)psi.pr_size * 1024;
            result.ResidentSetSize = (nuint)psi.pr_rssize * 1024;
            result.StartTime.TvSec = psi.pr_start.tv_sec;
            result.StartTime.TvNsec = psi.pr_start.tv_nsec;
            result.CpuTotalTime.TvSec = psi.pr_time.tv_sec;
            result.CpuTotalTime.TvNsec = psi.pr_time.tv_nsec;
            result.Priority = psi.pr_lwp.pr_pri;
            result.NiceVal  = psi.pr_lwp.pr_nice;

            result.Args = Encoding.UTF8.GetString(psi.pr_psargs.AsSpan());

            return true;
        }

        internal static bool TryReadRawPSInfo(int pid, Span<byte> buffer)
        {
            try
            {
                string fileName = GetInfoFilePathForProcess(pid);
                using FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                fs.ReadExactly(buffer);
                return true;
            }
            catch (Exception e)
            {
                Debug.Fail($"Failed to read process info: {e}");
                return false;
            }
        }
    }
}
