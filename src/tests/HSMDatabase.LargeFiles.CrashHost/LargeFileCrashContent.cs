using System;
using HSMDatabase.AccessManager.LargeFiles;

/// <summary>What the crash host uploads, shared with the test that checks it (linked into the test project).</summary>
internal static class LargeFileCrashContent
{
    public static readonly DateTime Modified = new(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc);

    public static LargeFileUploadKey Key(Guid product, Guid sensor, int pieces, int pieceSize) =>
        new(product, sensor, "crash.bin", (long)pieces * pieceSize, Modified, "crash-test");

    public static byte[] Piece(long number, int pieceSize)
    {
        var bytes = new byte[pieceSize];
        new Random((int)number + 1).NextBytes(bytes);
        return bytes;
    }
}
