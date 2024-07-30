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
        /// Attempts to get status info for the specified thread ID.
        /// </summary>
        /// <param name="pid">PID of the process to read status info for.</param>
        /// <param name="tid">TID of the thread to read status info for.</param>
        /// <param name="result">The pointer to ThreadInfo instance.</param>
        /// <returns>
        /// true if the thread info was read; otherwise, false.
        /// </returns>

        // ProcessManager.SunOS.cs calls this
        // "unsafe" due to use of fixed-size buffers
        internal static unsafe bool TryGetThreadInfoById(int pid, int tid, out ThreadInfo result)
        {
            result = default;
            bool ret = false;
            string fileName = "?";
            IntPtr ptr = 0;

            try
            {
                fileName = GetInfoFilePathForThread(pid, tid);
                int size = Marshal.SizeOf<lwpsinfo>();
                ptr = Marshal.AllocHGlobal(size);

                BinaryReader br = new BinaryReader(File.OpenRead(fileName));
                byte[] buf = br.ReadBytes(size);
                Marshal.Copy(buf, 0, ptr, size);

                procfs.lwpsinfo pr_lwp = Marshal.PtrToStructure<lwpsinfo>(ptr);

                result.Tid      = pr_lwp.pr_lwpid;
                result.Priority = pr_lwp.pr_pri;
                result.NiceVal  = (int)pr_lwp.pr_nice;
                result.Status   = (char)pr_lwp.pr_sname;
                result.StartTime.TvSec = pr_lwp.pr_start.tv_sec;
                result.StartTime.TvNsec = pr_lwp.pr_start.tv_nsec;
                result.CpuTotalTime.TvSec = pr_lwp.pr_time.tv_sec;
                result.CpuTotalTime.TvNsec = pr_lwp.pr_time.tv_nsec;

                ret = true;
            }
            catch (Exception e)
            {
                Debug.Fail($"Failed to read \"{fileName}\": {e}");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return ret;
        }

    }
}
