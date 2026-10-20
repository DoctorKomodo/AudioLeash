using AudioLeash;

namespace AudioLeash.Tests;

public class KeepAliveRowsTests
{
    private static DeviceRow Row(string id, string name, bool connected = true) => new(id, name, null, connected);

    [Fact]
    public void Build_MarksEnabledDevices()
    {
        var rows = KeepAliveRows.Build([Row("A", "Alpha"), Row("B", "Beta")], [new RankedDevice("A", "Alpha")]);
        Assert.True(rows.Single(r => r.Device.Id == "A").Enabled);
        Assert.False(rows.Single(r => r.Device.Id == "B").Enabled);
    }

    [Fact]
    public void Build_EnabledDeviceUnknownToWindows_IsListedDisconnectedAndEnabled()
    {
        var row = Assert.Single(KeepAliveRows.Build([], [new RankedDevice("X", "Old AVR", "NVIDIA")]));
        Assert.Equal(new DeviceRow("X", "Old AVR", "NVIDIA", IsConnected: false), row.Device);
        Assert.True(row.Enabled);
    }

    [Fact]
    public void Build_KnownDeviceWinsOverSavedName()
    {
        var row = Assert.Single(KeepAliveRows.Build([Row("A", "New name")], [new RankedDevice("A", "Old name")]));
        Assert.Equal("New name", row.Device.Name);
        Assert.True(row.Device.IsConnected);
    }

    [Fact]
    public void Build_SortsByNameCaseInsensitive()
    {
        var rows = KeepAliveRows.Build([Row("1", "b"), Row("2", "A"), Row("3", "c")], []);
        Assert.Equal(new[] { "A", "b", "c" }, rows.Select(r => r.Device.Name));
    }

    [Fact]
    public void Build_DuplicateKnownIds_FirstWins()
    {
        var row = Assert.Single(KeepAliveRows.Build([Row("A", "First"), Row("A", "Second")], []));
        Assert.Equal("First", row.Device.Name);
    }

    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "On for 1 device")]
    [InlineData(2, "On for 2 devices")]
    public void Summary_Formats(int count, string expected)
    {
        Assert.Equal(expected, KeepAliveRows.Summary(count));
    }
}
