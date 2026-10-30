using System;
using Volo.Abp.ExceptionHandling;

namespace OpenTms.Integrations.Routing;

/// <summary>Why a routing engine gave no usable answer (the log and the exception carry this, never coordinates).</summary>
public enum RoutingFailure
{
    /// <summary>No answer within <c>Integrations:Routing:TimeoutSeconds</c>.</summary>
    Timeout = 1,

    /// <summary>The host could not be reached (DNS, connection refused, TLS).</summary>
    Unreachable = 2,

    /// <summary>An HTTP status that is neither success nor "no path": 5xx, 429, 401/403, an unexpected 4xx.</summary>
    HttpStatus = 3,

    /// <summary>A body that is not JSON or not the engine's shape.</summary>
    InvalidAnswer = 4
}

/// <summary>
/// The routing engine is down or misbehaving (constitution rule 3: external calls retry with backoff). Thrown by the
/// providers and passed through <see cref="IRoutingService"/> so the calculation job that asked fails, keeps the old
/// values, records <see cref="IntegrationsErrorCodes.RoutingUnavailable"/> on the derived-value state and is retried by
/// Hangfire with its backoff. Distinct from "not configured" and "no path", which stay a null answer. The message carries
/// the provider, the operation, the failure kind and the status code only: never coordinates, URLs, keys or bodies.
/// </summary>
public class RoutingUnavailableException : Exception, IHasErrorCode
{
    public RoutingUnavailableException(string provider, string operation, RoutingFailure failure, int? statusCode = null, Exception? innerException = null)
        : base(Describe(provider, operation, failure, statusCode), innerException)
    {
        Provider = provider;
        Operation = operation;
        Failure = failure;
        StatusCode = statusCode;
    }

    public string Provider { get; }

    public string Operation { get; }

    public RoutingFailure Failure { get; }

    public int? StatusCode { get; }

    public string Code => IntegrationsErrorCodes.RoutingUnavailable;

    private static string Describe(string provider, string operation, RoutingFailure failure, int? statusCode)
        => statusCode == null
            ? $"Routing provider {provider} is unavailable for {operation}: {failure}."
            : $"Routing provider {provider} is unavailable for {operation}: {failure} (HTTP {statusCode}).";
}
