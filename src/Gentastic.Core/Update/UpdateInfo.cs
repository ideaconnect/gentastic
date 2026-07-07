namespace Gentastic.Core.Update;

/// <summary>Result of an update check against the project's releases.</summary>
public sealed record UpdateInfo(
    Version CurrentVersion,
    Version? LatestVersion,
    string? LatestTag,
    string? LatestUrl)
{
    public bool UpdateAvailable => LatestVersion is not null && LatestVersion > CurrentVersion;
}
