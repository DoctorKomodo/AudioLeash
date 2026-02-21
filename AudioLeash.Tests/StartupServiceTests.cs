#nullable enable
using Microsoft.Win32;
using AudioLeash;

namespace AudioLeash.Tests;

/// <summary>
/// Tests use a dedicated registry subkey to avoid touching the real Run key.
/// The key is deleted in Dispose().
/// </summary>
public sealed class StartupServiceTests : IDisposable
{
    // Use a private, deletable test key well away from the real Run key.
    private const string TestKeyPath = @"Software\AudioLeash-Tests\Run";

    private StartupService Svc() => new(TestKeyPath);

    public void Dispose()
    {
        // Clean up the whole test subtree.
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\AudioLeash-Tests", throwOnMissingSubKey: false);
    }

    [Fact]
    public void IsEnabled_WhenValueAbsent_ReturnsFalse()
    {
        Assert.False(Svc().IsEnabled);
    }

    [Fact]
    public void Enable_ThenIsEnabled_ReturnsTrue()
    {
        var svc = Svc();
        svc.Enable(@"C:\test\AudioLeash.exe");
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public void Disable_AfterEnable_IsEnabledReturnsFalse()
    {
        var svc = Svc();
        svc.Enable(@"C:\test\AudioLeash.exe");
        svc.Disable();
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public void Disable_WhenNeverEnabled_DoesNotThrow()
    {
        var svc = Svc();
        var ex = Record.Exception(() => svc.Disable());
        Assert.Null(ex);
    }
}
