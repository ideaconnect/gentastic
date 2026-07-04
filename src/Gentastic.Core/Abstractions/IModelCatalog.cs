using Gentastic.Core.Models;

namespace Gentastic.Core.Abstractions;

/// <summary>The curated set of models the app knows how to download and run.</summary>
public interface IModelCatalog
{
    IReadOnlyList<ModelSpec> GetAvailableModels();

    ModelSpec? FindById(string id);
}
