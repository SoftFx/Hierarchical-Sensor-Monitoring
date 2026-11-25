using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions.Files
{
    // A frame of more than 64 KiB on a file connection that holds no upload.
    internal sealed class FileFrameNotAllowedException() : Exception("A frame of more than 64 KiB arrived on a file connection that holds no upload.");


    // The receive buffers of one file connection (#1884). A frame of up to 64 KiB (a begin, an end, a
    // ping, a probe) comes from the shared pool, as on every connection. A larger frame, a piece, is
    // allowed only while the connection holds an upload, and is then an exact allocation of its own,
    // never the shared pool, and at once of the largest frame (`maxFrameBytes` + 1), so the receive never
    // grows it again: one allocation per piece, and the memory of file frames is at most one frame per
    // upload in progress, which the store bounds (LargeFiles.MaxUploadsInProgress). There is no wait and no
    // per-key rule. `large` is called once a frame outgrows the free allowance (its deadline starts).
    internal sealed class FileFrameMemory(int maxFrameBytes, Func<bool> holdsUpload, Action large = null) : IFrameMemory
    {
        public const int FreeAllowance = 64 * 1024;

        private int _large;


        public ValueTask<byte[]> RentAsync(int size, CancellationToken token)
        {
            if (size <= FreeAllowance)
                return ValueTask.FromResult(ArrayPool<byte>.Shared.Rent(size));

            if (!holdsUpload())
                throw new FileFrameNotAllowedException();

            if (Interlocked.Exchange(ref _large, 1) == 0)
                large?.Invoke();

            return ValueTask.FromResult(GC.AllocateUninitializedArray<byte>(Math.Max(size, maxFrameBytes + 1)));
        }

        public void Return(byte[] buffer)
        {
            if (buffer.Length <= FreeAllowance)
                ArrayPool<byte>.Shared.Return(buffer);
        }

        public void EndFrame() => Interlocked.Exchange(ref _large, 0);
    }
}
