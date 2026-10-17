#nullable enable
namespace AudioLeash;

/// <summary>
/// One entry in a ranked device priority list (position in the list is the rank).
/// The name and description are cached so unplugged devices can still be displayed.
/// </summary>
public sealed record RankedDevice(string Id, string Name, string? Description = null);
