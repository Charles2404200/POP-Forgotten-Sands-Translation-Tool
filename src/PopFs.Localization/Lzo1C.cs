namespace PopFs.Localization;

// LZO1C bitstream decoder for algorithm 5 in this game.
// Adapted from the LZO 2.x reference implementation by Markus F.X.J. Oberhumer
// (GPL-2.0-or-later); see the repository's COPYING file.
internal static class Lzo1C
{
    public static void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var input = 0;
        var output = 0;
        var afterLiteral = false;

        while (input < source.Length)
        {
            var marker = source[input++];
            if (marker < 32 && !afterLiteral)
            {
                var count = (int)marker;
                if (count == 0)
                {
                    count = ReadByte(source, ref input);
                    if (count >= 248)
                    {
                        count -= 248;
                        count = count == 0 ? 280 : 256 << count;
                        CopyLiteral(source, destination, ref input, ref output, count);
                        // Long R0 runs return to the outer marker loop.
                        continue;
                    }
                    else count += 32;
                }
                CopyLiteral(source, destination, ref input, ref output, count);
                afterLiteral = true;
                continue;
            }

            if (marker < 32 && afterLiteral)
            {
                var distance = 1 + marker + (ReadByte(source, ref input) << 5);
                CopyMatch(destination, ref output, distance, 3);
                CopyLiteral(source, destination, ref input, ref output, 1);
                continue;
            }

            afterLiteral = false;
            if (marker >= 64)
            {
                var distance = 1 + (marker & 31) + (ReadByte(source, ref input) << 5);
                CopyMatch(destination, ref output, distance, (marker >> 5) + 1);
                continue;
            }

            var lengthCode = marker & 31;
            if (lengthCode == 0)
            {
                int extension;
                do
                {
                    extension = ReadByte(source, ref input);
                    lengthCode = checked(lengthCode + (extension == 0 ? 255 : extension));
                } while (extension == 0);
                lengthCode += 31;
            }

            var low = ReadByte(source, ref input);
            var high = ReadByte(source, ref input);
            var distance3 = (low & 63) + (high << 6);
            if (distance3 == 0 && lengthCode == 1)
            {
                if (input != source.Length || output != destination.Length)
                    throw new InvalidDataException("LZO1C ended at an unexpected position.");
                return;
            }
            CopyMatch(destination, ref output, distance3, lengthCode + 3);
            var trailing = low >> 6;
            if (trailing != 0)
            {
                CopyLiteral(source, destination, ref input, ref output, trailing);
                afterLiteral = true;
            }
        }

        throw new InvalidDataException("LZO1C end marker was not found.");

    }

    private static int ReadByte(ReadOnlySpan<byte> source, ref int input)
    {
        if (input >= source.Length) throw new InvalidDataException("LZO1C input overrun.");
        return source[input++];
    }

    private static void CopyLiteral(ReadOnlySpan<byte> source, Span<byte> destination,
        ref int input, ref int output, int count)
    {
        if (count < 0 || source.Length - input < count || destination.Length - output < count)
            throw new InvalidDataException($"LZO1C literal overrun (input={input}/{source.Length}, output={output}/{destination.Length}, count={count}).");
        source.Slice(input, count).CopyTo(destination[output..]);
        input += count;
        output += count;
    }

    private static void CopyMatch(Span<byte> destination, ref int output, int distance, int count)
    {
        if (distance <= 0 || distance > output || count < 0 || destination.Length - output < count)
            throw new InvalidDataException("LZO1C match overrun.");
        for (var i = 0; i < count; i++)
        {
            destination[output] = destination[output - distance];
            output++;
        }
    }
}
