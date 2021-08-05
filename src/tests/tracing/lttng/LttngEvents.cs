// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.Tracing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lttng.Tests
{
    internal sealed class BashEmittingEventListener : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
            {
                EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords) (
                    0x00001000000 /* type names */ | 0x00000400000 /* range data */ | 0x00000100000 /* heap walking */ | 0x00000080000 /* BulkType events */));

                Console.WriteLine("events=()");
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            StringBuilder keysBuilder = new StringBuilder();
            StringBuilder valuesBuilder = new StringBuilder();
            for (int i = 0; i < eventData.Payload.Count; i++)
            {
                keysBuilder.Append($"'{eventData.PayloadNames[i]}' ");
                valuesBuilder.Append($"'{eventData.Payload[i]}' ");
            }

            Console.WriteLine("events+=(\"Name='{0}' Keys=({1}) VALUES=({2})\")", eventData.EventName,
                keysBuilder.ToString().Replace("\"", ".*"),
                valuesBuilder.ToString().Replace("\"", ".*"));
        }
    }

    static class LttngEvents
    {
        static async Task<int> Main()
        {
            using (_ = new BashEmittingEventListener())
            {
                for (int i = 0; i < 10; i++)
                {
                    Thread t = new Thread(()=> {});
                }

                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                return 0;
            }
        }
    }
}
