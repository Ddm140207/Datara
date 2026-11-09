namespace Datara.Application.Ai;

/// <summary>
/// Builds the compact, token-bounded JSON context handed to the LLM. This is the
/// mechanism that stops the entire dataset being sent to a model: only computed
/// statistics, column metadata, issues and a handful of sample rows are included.
/// </summary>
public interface IDatasetContextBuilder
{
    /// <summary>Serialises a profile into the context object sent to the provider.</summary>
    string BuildContext(DatasetProfileResponse profile, string datasetName);

    /// <summary>Human-readable context, used for logging and for the fallback path.</summary>
    string BuildReadableContext(DatasetProfileResponse profile, string datasetName);
}