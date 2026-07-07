using Gentastic.Core.Update;

namespace Gentastic.Core.Abstractions;

/// <summary>Checks whether a newer release of the app is available.</summary>
public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(CancellationToken ct = default);
}
