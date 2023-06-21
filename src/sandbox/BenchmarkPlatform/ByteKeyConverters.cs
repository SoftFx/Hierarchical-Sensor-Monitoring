using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace TestLevelDB;

[MemoryDiagnoser]
public class ByteKeyConverters
{
    [Params(1_000_000, 10_000_000)] 
    public int N;

    [Benchmark]
    public List<byte[]> SpanConverter()
    {
        List<byte[]> results = new(N);
        Span<byte> result = stackalloc byte[16 + sizeof(long)];
        for (long i = 0; i < N; i++)
        {
            var guid = Guid.NewGuid();
            guid.TryWriteBytes(result);
            BitConverter.TryWriteBytes(result[16..], N);

            results.Add(result.ToArray());
            result.Clear();
        }

        return results;
    }

    [Benchmark]
    public List<byte[]> BlockCopyConverter()
    {
        List<byte[]> results = new(N);

        for (long i = 0; i < N; i++)
        {
            var guid = Guid.NewGuid();

            var guidBytes = guid.ToByteArray();
            var timeBytes = BitConverter.GetBytes(N);
            var result = new byte[guidBytes.Length + timeBytes.Length];

            Buffer.BlockCopy(guidBytes, 0, result, 0, guidBytes.Length);
            Buffer.BlockCopy(timeBytes, 0, result, guidBytes.Length, timeBytes.Length);

            results.Add(result);
        }

        return results;
    }

    [Benchmark]
    public List<byte[]> StringConverter()
    {
        List<byte[]> results = new(N);
        for (long i = 0; i < N; i++)
        {
            var guid = Guid.NewGuid();
            results.Add(Encoding.UTF8.GetBytes($"{guid.ToString()}_{N:D19}"));
        }

        return results;
    }
}