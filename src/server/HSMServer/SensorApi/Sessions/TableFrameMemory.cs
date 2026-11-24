using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions
{
    // Where a session's receive buffers come from once a frame outgrows the session's own 4 KiB.
    internal interface IFrameMemory
    {
        ValueTask<byte[]> RentAsync(int size, CancellationToken token);

        void Return(byte[] buffer);
    }


    // The ordinary session: the shared pool, as before #1845. Its frames are at most
    // CollectorSessions.MaxFrameBytes.
    internal sealed class SharedPoolFrameMemory : IFrameMemory
    {
        public static SharedPoolFrameMemory Instance { get; } = new();

        public ValueTask<byte[]> RentAsync(int size, CancellationToken token) => ValueTask.FromResult(ArrayPool<byte>.Shared.Rent(size));

        public void Return(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);
    }


    // The wait for the table frame budget took longer than its bound.
    internal sealed class TableFrameBudgetTimeoutException() : Exception("The wait for the table frame budget took longer than its bound.");


    // The server-wide budget of bytes that table connections hold for frames being received and
    // waiting for their put (#1845, CollectorSessions.MaxTableBytesInFlight). A take waits, first
    // come first served, until the bytes are free; nothing ever takes more than the capacity.
    internal sealed class TableFrameBudget(long capacity)
    {
        private readonly object _lock = new();
        private readonly LinkedList<Waiter> _waiters = new();
        private long _inUse;


        public long Capacity { get; } = capacity;

        public long InUse
        {
            get
            {
                lock (_lock)
                    return _inUse;
            }
        }

        public int Waiting
        {
            get
            {
                lock (_lock)
                    return _waiters.Count;
            }
        }


        // True when the bytes were taken at once. Never ahead of a waiter.
        public bool TryTake(long bytes)
        {
            lock (_lock)
            {
                if (_waiters.Count > 0 || _inUse + bytes > Capacity)
                    return false;

                _inUse += bytes;
                return true;
            }
        }

        // Waits until the bytes are free and takes them. A cancelled wait takes nothing.
        public async Task TakeAsync(long bytes, CancellationToken token)
        {
            if (bytes > Capacity)
                throw new ArgumentOutOfRangeException(nameof(bytes), bytes, $"more than the budget of {Capacity} bytes");

            Waiter waiter;
            LinkedListNode<Waiter> node;

            lock (_lock)
            {
                if (_waiters.Count == 0 && _inUse + bytes <= Capacity)
                {
                    _inUse += bytes;
                    return;
                }

                waiter = new Waiter(bytes);
                node = _waiters.AddLast(waiter);
            }

            using (token.Register(() => Cancel(node)))
                await waiter.Granted.Task.ConfigureAwait(false);
        }

        public void Release(long bytes)
        {
            if (bytes <= 0)
                return;

            lock (_lock)
                _inUse -= bytes;

            Wake();
        }

        private void Cancel(LinkedListNode<Waiter> node)
        {
            lock (_lock)
            {
                // Granted already: the bytes are the waiter's, and its caller releases them.
                if (node.List is null)
                    return;

                _waiters.Remove(node);
            }

            node.Value.Granted.TrySetCanceled();

            // The head may have been the one holding the others back.
            Wake();
        }

        // Grants the waiters at the head that fit, in order: a large waiter at the head is not
        // overtaken by smaller ones behind it.
        private void Wake()
        {
            List<Waiter> granted = null;

            lock (_lock)
            {
                while (_waiters.First is { } first && _inUse + first.Value.Bytes <= Capacity)
                {
                    _inUse += first.Value.Bytes;
                    _waiters.RemoveFirst();
                    (granted ??= []).Add(first.Value);
                }
            }

            if (granted is not null)
                foreach (var waiter in granted)
                    waiter.Granted.TrySetResult();
        }


        private sealed class Waiter(long bytes)
        {
            public long Bytes { get; } = bytes;

            public TaskCompletionSource Granted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }


    // The receive buffers of one table connection (#1845). A frame that fits the free allowance
    // (64 KiB) uses the shared pool and costs no budget: idle connections and the usual blocks of
    // tens of kilobytes. A frame that outgrows it first reserves, from the server-wide budget, what
    // the largest frame can cost (its buffer, at most MaxTableFrameBytes + 1, and the copy the put
    // takes, at most MaxTableFrameBytes), and only then gets a buffer: an exact allocation, never
    // the shared pool, so a large frame leaves nothing pooled behind. The reservation is held until
    // the frame is done (its put answered) and is taken while holding nothing else, so connections
    // waiting for it cannot hold each other up.
    internal sealed class TableFrameMemory(TableFrameBudget budget, int maxFrameBytes, Func<CancellationToken, Task> waitForBudget) : IFrameMemory, IDisposable
    {
        public const int FreeAllowance = 64 * 1024;

        private long _reserved;


        // What one large frame reserves.
        public static long ReservationFor(int maxFrameBytes) => 2L * maxFrameBytes + 1;

        public long Reserved => Interlocked.Read(ref _reserved);


        public async ValueTask<byte[]> RentAsync(int size, CancellationToken token)
        {
            if (size <= FreeAllowance)
                return ArrayPool<byte>.Shared.Rent(size);

            if (Interlocked.Read(ref _reserved) == 0)
            {
                var reservation = ReservationFor(maxFrameBytes);

                if (!budget.TryTake(reservation))
                    await waitForBudget(token).ConfigureAwait(false);

                Interlocked.Exchange(ref _reserved, reservation);
            }

            return GC.AllocateUninitializedArray<byte>(size);
        }

        public void Return(byte[] buffer)
        {
            // A large buffer is the garbage collector's; only the pooled ones go back.
            if (buffer.Length <= FreeAllowance)
                ArrayPool<byte>.Shared.Return(buffer);
        }

        // The frame is done: its buffer and the put's copy are no longer used.
        public void EndFrame() => budget.Release(Interlocked.Exchange(ref _reserved, 0));

        public void Dispose() => EndFrame();
    }
}
