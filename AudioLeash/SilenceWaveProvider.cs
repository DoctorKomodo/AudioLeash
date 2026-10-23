#nullable enable
using System;
using NAudio.Wave;

namespace AudioLeash;

/// <summary>
/// An endless stream of digital silence (keep-alive "Zero" mode). All-zero bytes are silence
/// in both PCM and IEEE float, so this works in any format. Fills buffers without allocating.
/// </summary>
internal sealed class SilenceWaveProvider(WaveFormat format) : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = format;

    public int Read(byte[] buffer, int offset, int count)
    {
        buffer.AsSpan(offset, count).Clear();
        return count;
    }
}
