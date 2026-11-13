using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace HomeManagementClient.Services;

public static class MjpegReader
{
    private static ReadOnlySpan<byte> StartOfImage => [0xFF, 0xD8];

    private static ReadOnlySpan<byte> EndOfImage => [0xFF, 0xD9];

    private const int MaxFrameSize = 16 * 1024 * 1024;

    public static async IAsyncEnumerable<byte[]> ReadFramesAsync(Stream stream, [EnumeratorCancellation] CancellationToken token = default)
    {
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 64 * 1024));
        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(token);
                var buffer = result.Buffer;

                while (TryReadFrame(ref buffer, out var frame))
                {
                    yield return frame;
                }

                if (buffer.Length > MaxFrameSize)
                {
                    throw new InvalidDataException("MJPEG frame is too large.");
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    yield break;
                }
            }
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    private static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out byte[] frame)
    {
        frame = [];
        var sequenceReader = new SequenceReader<byte>(buffer);
        if (!sequenceReader.TryAdvanceTo(StartOfImage[0], advancePastDelimiter: false))
        {
            buffer = buffer.Slice(Math.Max(0, buffer.Length - 1));
            return false;
        }

        while (true)
        {
            var start = sequenceReader.Position;
            if (sequenceReader.IsNext(StartOfImage, advancePast: true))
            {
                if (!sequenceReader.TryReadTo(out ReadOnlySequence<byte> _, EndOfImage, advancePastDelimiter: true))
                {
                    buffer = buffer.Slice(start);
                    return false;
                }

                frame = buffer.Slice(start, sequenceReader.Position).ToArray();
                buffer = buffer.Slice(sequenceReader.Position);
                return true;
            }

            sequenceReader.Advance(1);
            if (!sequenceReader.TryAdvanceTo(StartOfImage[0], advancePastDelimiter: false))
            {
                buffer = buffer.Slice(Math.Max(0, buffer.Length - 1));
                return false;
            }
        }
    }
}
