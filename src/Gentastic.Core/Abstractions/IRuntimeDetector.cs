using Gentastic.Core.Models;

namespace Gentastic.Core.Abstractions;

/// <summary>Inspects the machine and recommends the best available generation backend/device.</summary>
public interface IRuntimeDetector
{
    HardwareProfile Detect();
}
