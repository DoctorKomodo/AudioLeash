#nullable enable
using AudioLeash;

namespace AudioLeash.Tests;

public class ClearSelectionMessageTests
{
    [Fact]
    public void ClearSelectionMessage_BothLegacy_SaysAutoRestoreDisabled()
        => Assert.Equal("Auto-restore disabled. Select a device to re-enable.",
                        AudioLeashContext.ClearSelectionMessage(playbackLegacy: true, captureLegacy: true));

    [Fact]
    public void ClearSelectionMessage_OnlyPlaybackLegacy_NamesPlaybackAndRecordingStillEnforces()
        => Assert.Equal("Playback auto-restore disabled. Recording still follows your priority order.",
                        AudioLeashContext.ClearSelectionMessage(playbackLegacy: true, captureLegacy: false));

    [Fact]
    public void ClearSelectionMessage_OnlyRecordingLegacy_NamesRecordingAndPlaybackStillEnforces()
        => Assert.Equal("Recording auto-restore disabled. Playback still follows your priority order.",
                        AudioLeashContext.ClearSelectionMessage(playbackLegacy: false, captureLegacy: true));
}
