using SharedKernel;
using Typesense;

namespace SearchService.Infrastructure.Typesense;

public static class TypesenseErrorMapper
{
    /// <summary>
    /// Maps a Typesense exception to a Result-pattern <see cref="Error"/>.
    /// <para>
    /// Transient failures (network, 503, conflict, unknown) are flagged via
    /// <see cref="Error.AsTransient"/> so the platform Wolverine error policy
    /// converts them to <c>TransientException</c> and retries with backoff.
    /// </para>
    /// <para>
    /// Permanent failures (bad-request, schema mismatch, auth misconfig, invalid
    /// argument) keep the default permanent behaviour → handlers throw
    /// <c>PermanentException</c> → message goes straight to the dead-letter
    /// queue without burning retry budget. Documented in
    /// <c>WolverineErrorHandlingExtensions.ConfigureStandardErrorPolicies</c>.
    /// </para>
    /// </summary>
    public static Error ToError(Exception ex) => ex switch
    {
        // PERMANENT — invalid payload / schema mismatch / auth bug. Retry is pointless.
        TypesenseApiBadRequestException
            => Error.Validation("search.typesense.bad_request", "Typesense rejected request parameters"),

        TypesenseApiUnprocessableEntityException
            => Error.Validation("search.typesense.unprocessable_entity", "Typesense cannot process the provided data"),

        TypesenseApiUnauthorizedException
            => Error.Failure("search.typesense.unauthorized", "Typesense authorization failed"),

        TypesenseApiNotFoundException
            => Error.NotFound("search.typesense.not_found", "Document or collection was not found in Typesense"),

        ArgumentException
            => Error.Validation("search.validation.failed", "Invalid search provider arguments"),

        // TRANSIENT — network blip / 503 / unknown. Retry per platform policy.
        TypesenseApiConflictException
            => Error.Conflict("search.typesense.conflict", "Typesense request conflicts with current state")
                .AsTransient(),

        TypesenseApiServiceUnavailableException
            => Error.Failure("search.typesense.unavailable", "Typesense service is temporarily unavailable")
                .AsTransient(),

        HttpRequestException
            => Error.Failure("search.network.issue", "Network issue while communicating with Typesense")
                .AsTransient(),

        OperationCanceledException
            => Error.Failure("search.operation.cancelled", "Search operation timed out")
                .AsTransient(),

        _ => Error.Failure("search.typesense.unknown", "Unknown Typesense error")
            .AsTransient()
    };
}
