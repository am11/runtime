// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TaskSourceCodes = System.IO.Strategies.FileStreamHelpers.TaskSourceCodes;

namespace System.IO.Strategies
{
    internal sealed partial class Net5CompatFileStreamStrategy : FileStreamStrategy
    {
        // This is an internal object extending TaskCompletionSource with fields
        // for all of the relevant data necessary to complete the IO operation.
        // This is used by IOCallback and all of the async methods.
        private unsafe class CompletionSource : TaskCompletionSource<int>
        {
            internal static readonly unsafe IOCompletionCallback s_ioCallback = IOCallback;

            private static Action<object?>? s_cancelCallback;

            private readonly Net5CompatFileStreamStrategy _strategy;
            private readonly int _numBufferedBytes;
            private CancellationTokenRegistration _cancellationRegistration;
#if DEBUG
            private bool _cancellationHasBeenRegistered;
#endif
            private NativeOverlapped* _overlapped; // Overlapped class responsible for operations in progress when an appdomain unload occurs
            private long _result; // Using long since this needs to be used in Interlocked APIs

            // Using RunContinuationsAsynchronously for compat reasons (old API used Task.Factory.StartNew for continuations)
            internal CompletionSource(Net5CompatFileStreamStrategy strategy, PreAllocatedOverlapped? preallocatedOverlapped,
                int numBufferedBytes, byte[]? bytes) : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                _numBufferedBytes = numBufferedBytes;
                _strategy = strategy;
                _result = TaskSourceCodes.NoResult;

                // The _preallocatedOverlapped is null if the internal buffer was never created, so we check for
                // a non-null bytes before using the stream's _preallocatedOverlapped
                _overlapped = bytes != null && strategy.CompareExchangeCurrentOverlappedOwner(this, null) == null ?
                    strategy._fileHandle.ThreadPoolBinding!.AllocateNativeOverlapped(preallocatedOverlapped!) : // allocated when buffer was created, and buffer is non-null
                    strategy._fileHandle.ThreadPoolBinding!.AllocateNativeOverlapped(s_ioCallback, this, bytes);
                Debug.Assert(_overlapped != null, "AllocateNativeOverlapped returned null");
            }

            internal NativeOverlapped* Overlapped => _overlapped;

            public void SetCompletedSynchronously(int numBytes)
            {
                ReleaseNativeResource();
                TrySetResult(numBytes + _numBufferedBytes);
            }

            public void RegisterForCancellation(CancellationToken cancellationToken)
            {
#if DEBUG
                Debug.Assert(cancellationToken.CanBeCanceled);
                Debug.Assert(!_cancellationHasBeenRegistered, "Cannot register for cancellation twice");
                _cancellationHasBeenRegistered = true;
#endif

                // Quick check to make sure the IO hasn't completed
                if (_overlapped != null)
                {
                    Action<object?>? cancelCallback = s_cancelCallback ??= Cancel;

                    // Register the cancellation only if the IO hasn't completed
                    long packedResult = Interlocked.CompareExchange(ref _result, TaskSourceCodes.RegisteringCancellation, TaskSourceCodes.NoResult);
                    if (packedResult == TaskSourceCodes.NoResult)
                    {
                        _cancellationRegistration = cancellationToken.UnsafeRegister(cancelCallback, this);

                        // Switch the result, just in case IO completed while we were setting the registration
                        packedResult = Interlocked.Exchange(ref _result, TaskSourceCodes.NoResult);
                    }
                    else if (packedResult != TaskSourceCodes.CompletedCallback)
                    {
                        // Failed to set the result, IO is in the process of completing
                        // Attempt to take the packed result
                        packedResult = Interlocked.Exchange(ref _result, TaskSourceCodes.NoResult);
                    }

                    // If we have a callback that needs to be completed
                    if ((packedResult != TaskSourceCodes.NoResult) && (packedResult != TaskSourceCodes.CompletedCallback) && (packedResult != TaskSourceCodes.RegisteringCancellation))
                    {
                        CompleteCallback((ulong)packedResult);
                    }
                }
            }

            internal virtual void ReleaseNativeResource()
            {
                // Ensure that cancellation has been completed and cleaned up.
                _cancellationRegistration.Dispose();

                // Free the overlapped.
                // NOTE: The cancellation must *NOT* be running at this point, or it may observe freed memory
                // (this is why we disposed the registration above).
                if (_overlapped != null)
                {
                    _strategy._fileHandle.ThreadPoolBinding!.FreeNativeOverlapped(_overlapped);
                    _overlapped = null;
                }

                // Ensure we're no longer set as the current completion source (we may not have been to begin with).
                // Only one operation at a time is eligible to use the preallocated overlapped,
                _strategy.CompareExchangeCurrentOverlappedOwner(null, this);
            }

            // When doing IO asynchronously (i.e. _isAsync==true), this callback is
            // called by a free thread in the threadpool when the IO operation
            // completes.
            internal static void IOCallback(uint errorCode, uint numBytes, NativeOverlapped* pOverlapped)
            {
                // Extract the completion source from the overlapped.  The state in the overlapped
                // will either be a FileStreamStrategy (in the case where the preallocated overlapped was used),
                // in which case the operation being completed is its _currentOverlappedOwner, or it'll
                // be directly the FileStreamCompletionSource that's completing (in the case where the preallocated
                // overlapped was already in use by another operation).
                object? state = ThreadPoolBoundHandle.GetNativeOverlappedState(pOverlapped);
                Debug.Assert(state is Net5CompatFileStreamStrategy || state is CompletionSource);
                CompletionSource completionSource = state switch
                {
                    Net5CompatFileStreamStrategy strategy => strategy._currentOverlappedOwner!, // must be owned
                    _ => (CompletionSource)state
                };
                Debug.Assert(completionSource != null);
                Debug.Assert(completionSource._overlapped == pOverlapped, "Overlaps don't match");

                // Handle reading from & writing to closed pipes.  While I'm not sure
                // this is entirely necessary anymore, maybe it's possible for
                // an async read on a pipe to be issued and then the pipe is closed,
                // returning this error.  This may very well be necessary.
                ulong packedResult;
                if (errorCode != 0 && errorCode != Interop.Errors.ERROR_BROKEN_PIPE && errorCode != Interop.Errors.ERROR_NO_DATA)
                {
                    packedResult = ((ulong)TaskSourceCodes.ResultError | errorCode);
                }
                else
                {
                    packedResult = ((ulong)TaskSourceCodes.ResultSuccess | numBytes);
                }

                // Stow the result so that other threads can observe it
                // And, if no other thread is registering cancellation, continue
                if (TaskSourceCodes.NoResult == Interlocked.Exchange(ref completionSource._result, (long)packedResult))
                {
                    // Successfully set the state, attempt to take back the callback
                    if (Interlocked.Exchange(ref completionSource._result, TaskSourceCodes.CompletedCallback) != TaskSourceCodes.NoResult)
                    {
                        // Successfully got the callback, finish the callback
                        completionSource.CompleteCallback(packedResult);
                    }
                    // else: Some other thread stole the result, so now it is responsible to finish the callback
                }
                // else: Some other thread is registering a cancellation, so it *must* finish the callback
            }

            private void CompleteCallback(ulong packedResult)
            {
                // Free up the native resource and cancellation registration
                CancellationToken cancellationToken = _cancellationRegistration.Token; // access before disposing registration
                ReleaseNativeResource();

                // Unpack the result and send it to the user
                long result = (long)(packedResult & TaskSourceCodes.ResultMask);
                if (result == TaskSourceCodes.ResultError)
                {
                    int errorCode = unchecked((int)(packedResult & uint.MaxValue));
                    if (errorCode == Interop.Errors.ERROR_OPERATION_ABORTED)
                    {
                        TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
                    }
                    else
                    {
                        Exception e = Win32Marshal.GetExceptionForWin32Error(errorCode);
                        e.SetCurrentStackTrace();
                        TrySetException(e);
                    }
                }
                else
                {
                    Debug.Assert(result == TaskSourceCodes.ResultSuccess, "Unknown result");
                    TrySetResult((int)(packedResult & uint.MaxValue) + _numBufferedBytes);
                }
            }

            private static void Cancel(object? state)
            {
                // WARNING: This may potentially be called under a lock (during cancellation registration)

                Debug.Assert(state is CompletionSource, "Unknown state passed to cancellation");
                CompletionSource completionSource = (CompletionSource)state;
                Debug.Assert(completionSource._overlapped != null && !completionSource.Task.IsCompleted, "IO should not have completed yet");

                // If the handle is still valid, attempt to cancel the IO
                if (!completionSource._strategy._fileHandle.IsInvalid &&
                    !Interop.Kernel32.CancelIoEx(completionSource._strategy._fileHandle, completionSource._overlapped))
                {
                    int errorCode = Marshal.GetLastWin32Error();

                    // ERROR_NOT_FOUND is returned if CancelIoEx cannot find the request to cancel.
                    // This probably means that the IO operation has completed.
                    if (errorCode != Interop.Errors.ERROR_NOT_FOUND)
                    {
                        throw Win32Marshal.GetExceptionForWin32Error(errorCode);
                    }
                }
            }

            public static CompletionSource Create(Net5CompatFileStreamStrategy strategy, PreAllocatedOverlapped? preallocatedOverlapped,
                int numBufferedBytesRead, ReadOnlyMemory<byte> memory)
            {
                // If the memory passed in is the strategy's internal buffer, we can use the base FileStreamCompletionSource,
                // which has a PreAllocatedOverlapped with the memory already pinned.  Otherwise, we use the derived
                // MemoryFileStreamCompletionSource, which Retains the memory, which will result in less pinning in the case
                // where the underlying memory is backed by pre-pinned buffers.
                return preallocatedOverlapped != null && MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> buffer)
                    && preallocatedOverlapped.IsUserObject(buffer.Array) // preallocatedOverlapped is allocated when BufferedStream|Net5CompatFileStreamStrategy allocates the buffer
                    ? new CompletionSource(strategy, preallocatedOverlapped, numBufferedBytesRead, buffer.Array)
                    : new MemoryFileStreamCompletionSource(strategy, numBufferedBytesRead, memory);
            }
        }

        /// <summary>
        /// Extends <see cref="CompletionSource"/> with to support disposing of a
        /// <see cref="MemoryHandle"/> when the operation has completed.  This should only be used
        /// when memory doesn't wrap a byte[].
        /// </summary>
        private sealed class MemoryFileStreamCompletionSource : CompletionSource
        {
            private MemoryHandle _handle; // mutable struct; do not make this readonly

            internal MemoryFileStreamCompletionSource(Net5CompatFileStreamStrategy strategy, int numBufferedBytes, ReadOnlyMemory<byte> memory)
                : base(strategy, null, numBufferedBytes, null) // this type handles the pinning, so null is passed for bytes
            {
                _handle = memory.Pin();
            }

            internal override void ReleaseNativeResource()
            {
                _handle.Dispose();
                base.ReleaseNativeResource();
            }
        }
    }
}
