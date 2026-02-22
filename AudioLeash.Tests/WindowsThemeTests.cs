#nullable enable
using System;
using AudioLeash;

namespace AudioLeash.Tests;

// Note: deeper coverage of IsDarkMode (e.g. key-present/absent, value 0/1) would require
// abstracting the registry read behind an interface or delegate seam.  The tests below serve
// as smoke tests that verify cross-platform robustness and correct API surface.
public class WindowsThemeTests
{
    [Fact]
    public void IsDarkMode_ReturnsBoolWithoutThrowing()
    {
        // Verifies the property is reachable and does not throw on any platform
        // (returns false gracefully when the registry key is absent, e.g. on Linux CI).
        var result = WindowsTheme.IsDarkMode;
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void Changed_CanSubscribeAndUnsubscribeWithoutThrowing()
    {
        EventHandler handler = (_, _) => { };

        var ex = Record.Exception(() =>
        {
            WindowsTheme.Changed += handler;
            WindowsTheme.Changed -= handler;
        });

        Assert.Null(ex);
    }
}
