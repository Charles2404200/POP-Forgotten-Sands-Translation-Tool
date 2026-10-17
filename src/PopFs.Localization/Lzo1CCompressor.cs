using System.Runtime.InteropServices;

namespace PopFs.Localization;

internal sealed class Lzo1CCompressor : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CompressFunction(IntPtr source, UIntPtr sourceLength,
        IntPtr destination, ref UIntPtr destinationLength, IntPtr workMemory);

    private readonly IntPtr library;
    private readonly CompressFunction compress;

    public Lzo1CCompressor(string libraryPath)
    {
        library = NativeLibrary.Load(libraryPath);
        compress = Marshal.GetDelegateForFunctionPointer<CompressFunction>(
            NativeLibrary.GetExport(library, "lzo1c_999_compress"));
    }

    public byte[] Compress(ReadOnlySpan<byte> source)
    {
        var input = source.ToArray();
        var output = new byte[input.Length + input.Length / 16 + 256];
        var work = new byte[1024 * 1024];
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);
        var workHandle = GCHandle.Alloc(work, GCHandleType.Pinned);
        try
        {
            var length = (UIntPtr)output.Length;
            var status = compress(inputHandle.AddrOfPinnedObject(), (UIntPtr)input.Length,
                outputHandle.AddrOfPinnedObject(), ref length, workHandle.AddrOfPinnedObject());
            if (status != 0) throw new InvalidDataException($"LZO1C compressor returned {status}.");
            var packed = output.AsSpan(0, checked((int)length)).ToArray();
            var check = new byte[input.Length];
            Lzo1C.Decompress(packed, check);
            if (!check.AsSpan().SequenceEqual(input))
                throw new InvalidDataException("Fresh LZO1C chunk failed byte-for-byte verification.");
            return packed;
        }
        finally
        {
            workHandle.Free();
            outputHandle.Free();
            inputHandle.Free();
        }
    }

    public void Dispose() => NativeLibrary.Free(library);
}
