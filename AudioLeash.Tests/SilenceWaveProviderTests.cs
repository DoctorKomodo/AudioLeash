using AudioLeash;
using NAudio.Wave;

namespace AudioLeash.Tests;

public class SilenceWaveProviderTests
{
    [Fact]
    public void Read_ClearsRequestedRangeAndReturnsCount()
    {
        var buffer = Enumerable.Repeat((byte)0xAB, 16).ToArray();
        var provider = new SilenceWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));

        Assert.Equal(8, provider.Read(buffer, 4, 8));

        Assert.All(buffer[..4], b => Assert.Equal(0xAB, b));
        Assert.All(buffer[4..12], b => Assert.Equal(0, b));
        Assert.All(buffer[12..], b => Assert.Equal(0xAB, b));
    }

    [Fact]
    public void WaveFormat_IsTheOneGiven()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        Assert.Same(format, new SilenceWaveProvider(format).WaveFormat);
    }
}
