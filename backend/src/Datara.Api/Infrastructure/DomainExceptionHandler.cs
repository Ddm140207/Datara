using System.Net;
using System.Text.Json;
using Datara.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Datara.Api.Infrastructure;

/// <summary>
/// Translates the domain's expected exceptions into RFC 7807 problem responses.
/// Controllers therefore never contain try/catch for ordinary failures.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<DomainExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            ValidationFailedException => (StatusCodes.Status422UnprocessableEntity, "Validation failed", "validation_failed"),
            InvalidStateException => (StatusCodes.Status409Conflict, "Invalid state", "invalid_state"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", "not_found"),
            UnsupportedFileException => (StatusCodes.Status422UnprocessableEntity, "Unsupported file", "unsupported_file"),
            AnalysisFailedException => (StatusCodes.Status422UnprocessableEntity, "Analysis failed", "analysis_failed"),
            AnalysisServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Analysis service unavailable", "analysis_service_unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "unexpected_error"),
        };

        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Path}", httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation(
                "{Code} on {Path}: {Message}",
                code,
                httpContext.Request.Path,
                exception.Message);
        }

        httpContext.Response.StatusCode = status;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status >= 500 && !httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    ? "An unexpected error occurred. Please try again."
                    : exception.Message,
                Extensions =
                {
                    ["code"] = code,
                    ["traceId"] = httpContext.TraceIdentifier,
                },
            },
        });
    }
}

/// <summary>Well-known error codes mirrored from the domain exception hierarchy.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidState = "invalid_state";
    public const string NotFound = "not_found";
    public const string UnsupportedFile = "unsupported_file";
    public const string AnalysisFailed = "analysis_failed";
    public const string AnalysisServiceUnavailable = "analysis_service_unavailable";
}
