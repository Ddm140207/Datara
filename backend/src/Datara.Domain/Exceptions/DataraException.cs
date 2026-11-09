using Datara.Domain.Enums;

namespace Datara.Domain.Exceptions;

/// <summary>Base type for every expected, client-actionable failure.</summary>
public abstract class DataraException : Exception
{
    protected DataraException(string message) : base(message)
    {
    }
}

/// <summary>A client supplied input that failed validation.</summary>
public sealed class ValidationFailedException : DataraException
{
    public ValidationFailedException(string message) : base(message)
    {
    }
}

/// <summary>An upload violated a format, size or content rule.</summary>
public sealed class UnsupportedFileException : DataraException
{
    public UnsupportedFileException(string message) : base(message)
    {
    }
}

/// <summary>A dataset failed to profile. Carries the reason back to the caller.</summary>
public sealed class AnalysisFailedException : DataraException
{
    public AnalysisFailedException(string message) : base(message)
    {
    }

    public AnalysisFailedException(string message, Exception inner) : base(message)
    {
        Inner = inner;
    }

    public Exception? Inner { get; }
}

/// <summary>The analysis service could not be reached or returned an error.</summary>
public sealed class AnalysisServiceUnavailableException : DataraException
{
    public AnalysisServiceUnavailableException(string message) : base(message)
    {
    }

    public AnalysisServiceUnavailableException(string message, Exception inner) : base(message)
    {
        Inner = inner;
    }

    public Exception? Inner { get; }
}

/// <summary>The configured AI provider is unavailable or rejected the request.</summary>
public sealed class AiProviderUnavailableException : DataraException
{
    public AiProviderUnavailableException(string message) : base(message)
    {
    }

    public AiProviderUnavailableException(string message, Exception inner) : base(message)
    {
        Inner = inner;
    }

    public Exception? Inner { get; }
}

/// <summary>The requested resource does not exist or is not visible to the caller.</summary>
public sealed class NotFoundException : DataraException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string resource, object key)
        : base($"{resource} '{key}' was not found.")
    {
    }
}

/// <summary>An operation was requested against a resource in an incompatible state.</summary>
public sealed class InvalidStateException : DataraException
{
    public InvalidStateException(AnalysisStatus status, Guid datasetId)
        : base($"Dataset '{datasetId}' is {status} and cannot be analysed right now.")
    {
    }

    public InvalidStateException(string message) : base(message)
    {
    }
}