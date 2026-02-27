// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace System.Runtime.InteropServices
{
    public sealed partial class PosixSignalRegistration
    {
        // State machine for native handler installation:
        // 0 = Uninitialized, 1 = Registering (in-flight), 2 = Registered
        private static int s_ctrlHandlerState;

        // Per-signal token store using immutable linked lists (copy-on-write for heads)
        // Key: Windows console control event (dwCtrlType)
        // Value: linked list head (or null if no handlers)
        private static readonly ConcurrentDictionary<int, TokenNode?> s_handlers =
            new ConcurrentDictionary<int, TokenNode?>();

        // NOTE: _token field, constructor, Dispose(), and finalizer are defined
        // in another partial of this class. Do not redeclare them here.

        /// <summary>
        /// Windows-specific registration:
        ///  - Installs the native handler once (lock-free).
        ///  - Never unregisters at the OS level to avoid shutdown deadlocks.
        ///  - Adds the token to a per-signal immutable list.
        /// </summary>
        private static unsafe PosixSignalRegistration Register(
            PosixSignal signal,
            Action<PosixSignalContext> handler)
        {
            int signo = signal switch
            {
                PosixSignal.SIGINT => Interop.Kernel32.CTRL_C_EVENT,
                PosixSignal.SIGQUIT => Interop.Kernel32.CTRL_BREAK_EVENT,
                PosixSignal.SIGTERM => Interop.Kernel32.CTRL_SHUTDOWN_EVENT,
                PosixSignal.SIGHUP => Interop.Kernel32.CTRL_CLOSE_EVENT,
                _ => throw new PlatformNotSupportedException()
            };

            EnsureNativeHandlerInstalled();

            Token token = new Token(signal, signo, handler);
            PosixSignalRegistration registration = new PosixSignalRegistration(token);

            AddToken(signo, token);
            return registration;
        }

        /// <summary>
        /// Unregister the handler (called by Dispose and finalizer in the shared partial).
        /// Does not call SetConsoleCtrlHandler; only detaches our delegate snapshot.
        /// </summary>
        private void Unregister()
        {
            // _token is declared in the shared partial
            Token? token = Interlocked.Exchange(ref _token, null);
            if (token is null)
                return;

            RemoveToken(token.SigNo, token);
        }

        // Lock-free one-shot native handler installation using CAS state machine
        private static unsafe void EnsureNativeHandlerInstalled()
        {
            if (Volatile.Read(ref s_ctrlHandlerState) == 2)
                return;

            if (Interlocked.CompareExchange(ref s_ctrlHandlerState, 1, 0) == 0)
            {
                try
                {
                    // Register once, never unregister (avoids deadlock during shutdown)
                    bool ok = Interop.Kernel32.SetConsoleCtrlHandler(&HandlerRoutine, Add: true);
                    if (!ok)
                    {
                        Volatile.Write(ref s_ctrlHandlerState, 0);
                        throw Win32Marshal.GetExceptionForLastWin32Error();
                    }

                    Volatile.Write(ref s_ctrlHandlerState, 2);
                }
                catch
                {
                    Volatile.Write(ref s_ctrlHandlerState, 0);
                    throw;
                }
            }
        }

        // Lock-free insertion using linked list prepend and ConcurrentDictionary CAS
        private static void AddToken(int signo, Token token)
        {
            while (true)
            {
                if (s_handlers.TryGetValue(signo, out TokenNode? current))
                {
                    TokenNode updated = new TokenNode(token, current);
                    if (s_handlers.TryUpdate(signo, updated, current))
                        return;
                }
                else
                {
                    TokenNode initial = new TokenNode(token, next: null);
                    if (s_handlers.TryAdd(signo, initial))
                        return;
                }
            }
        }

        // Lock-free removal using linked list reconstruction
        private static void RemoveToken(int signo, Token token)
        {
            while (true)
            {
                if (!s_handlers.TryGetValue(signo, out TokenNode? current))
                    return;

                TokenNode? updated = RemoveFromList(current, token);
                if (updated is null)
                {
                    if (s_handlers.TryRemove(new KeyValuePair<int, TokenNode?>(signo, current)))
                        return;
                }
                else
                {
                    if (s_handlers.TryUpdate(signo, updated, current))
                        return;
                }
            }
        }

        // Reconstruct linked list without the target token (recursive)
        private static TokenNode? RemoveFromList(TokenNode? node, Token token)
        {
            if (node is null)
                return null;

            if (ReferenceEquals(node.Token, token))
                return node.Next;

            TokenNode? newNext = RemoveFromList(node.Next, token);
            if (ReferenceEquals(newNext, node.Next))
                return node; // no change needed below

            return new TokenNode(node.Token, newNext);
        }

        // Fully lock-free native callback reading immutable linked list snapshot
        [UnmanagedCallersOnly]
        private static Interop.BOOL HandlerRoutine(int dwCtrlType)
        {
            if (!s_handlers.TryGetValue(dwCtrlType, out TokenNode? node) || node is null)
                return Interop.BOOL.FALSE;

            PosixSignalContext context = new PosixSignalContext(0);

            // Iterate through linked list (newest-first, since we prepend)
            while (node is not null)
            {
                Token token = node.Token;
                context.Signal = token.Signal;
                token.Handler(context);
                node = node.Next;
            }

            return context.Cancel ? Interop.BOOL.TRUE : Interop.BOOL.FALSE;
        }

        // Immutable node in the linked list of tokens
        private sealed class TokenNode
        {
            public readonly Token Token;
            public readonly TokenNode? Next;

            public TokenNode(Token token, TokenNode? next)
            {
                Token = token;
                Next = next;
            }
        }
    }
}
