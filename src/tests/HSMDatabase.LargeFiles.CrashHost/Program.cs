using System;
using System.Security.Cryptography;
using System.Threading;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.LargeFiles;

// The child process of LargeFileStoreKillTests (#1883). Begins (or continues) one upload through the real store and
// appends its pieces in order, printing "ACK <n>" after the store acknowledged piece n, until every piece is in or the
// test kills the process. Arguments: <folder> <product id> <sensor id> <piece count> <piece size> [<pause piece>]: with a
// pause piece the host stops for good inside that piece's append, after the part is flushed and before the record is
// written, and prints "PAUSED <n>" (a kill there is a kill in the middle of a piece, for certain).
// The content of piece n is fixed by n (LargeFileCrashContent), so the test can check the part byte for byte.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length is not (5 or 6))
        {
            Console.Error.WriteLine("usage: <folder> <product id> <sensor id> <piece count> <piece size> [<pause piece>]");
            return 2;
        }

        var folder = args[0];
        var product = Guid.Parse(args[1]);
        var sensor = Guid.Parse(args[2]);
        var pieces = int.Parse(args[3]);
        var pieceSize = int.Parse(args[4]);
        var pausePiece = args.Length == 6 ? long.Parse(args[5]) : -1;
        var current = -1L;

        using var store = new LargeFileStore(folder)
        {
            Limits = new LargeFileStoreLimits { MinFreeBytes = 0 },
            FaultHook = point =>
            {
                if (point == LargeFileStore.FaultPartFlushed && current == pausePiece)
                {
                    Console.Out.WriteLine($"PAUSED {current}");
                    Console.Out.Flush();
                    Thread.Sleep(Timeout.Infinite);
                }
            },
        };

        var key = LargeFileCrashContent.Key(product, sensor, pieces, pieceSize);
        var begin = store.Begin(key, "bin", pieceSize, pieceSize);

        if (begin.Outcome is not (LargeFileBeginOutcome.Started or LargeFileBeginOutcome.Continue))
        {
            Console.Error.WriteLine($"begin: {begin.Outcome} {begin.Error}");
            return 3;
        }

        Console.Out.WriteLine($"BEGIN {begin.NextPiece}");
        Console.Out.Flush();

        for (var n = begin.NextPiece; n < pieces; n++)
        {
            var bytes = LargeFileCrashContent.Piece(n, pieceSize);
            current = n;
            var result = store.Append(begin.Lease, n, bytes, SHA256.HashData(bytes));

            if (result.Outcome != LargeFileAppendOutcome.Stored)
            {
                Console.Error.WriteLine($"append {n}: {result.Outcome}");
                return 4;
            }

            Console.Out.WriteLine($"ACK {n}");
            Console.Out.Flush();
        }

        Console.Out.WriteLine("DONE");
        Console.Out.Flush();

        return 0;
    }
}
