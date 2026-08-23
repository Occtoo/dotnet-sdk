using CSharpFunctionalExtensions;

namespace Occtoo;

/// <summary>
/// A failure returned by the SDK, carried in the failure track of
/// <see cref="Result{T,E}"/> rather than thrown.
/// </summary>
/// <remarks>
/// <para>
/// Every failure the SDK anticipates — anything that can go wrong talking to
/// Occtoo — is returned as <c>Result&lt;T, OcctooError&gt;</c> rather than
/// thrown. Exceptions are reserved for programming mistakes caught at startup
/// (dependency-injection wiring, options validation), cancellation, and
/// genuinely unexpected conditions.
/// </para>
/// <para>
/// The hierarchy is what consumers branch on. <see cref="TransientError"/> and
/// its descendants are safe to retry; everything else means the request as
/// written will keep failing until something is changed:
/// </para>
/// <code>
/// var message = error switch
/// {
///     RateLimitError { RetryAfter.HasValue: true } e => $"back off {e.RetryAfter.Value}",
///     TransientError => "retry with backoff",
///     ValidationError e => $"fix the payload: {e.Message}",
///     AuthenticationError => "check the credential",
///     _ => error.Message,
/// };
/// </code>
/// </remarks>
/// <param name="Message">A human-readable description of what went wrong.</param>
public abstract record OcctooError(string Message)
{
    /// <summary>The error type and message, for logs.</summary>
    public sealed override string ToString() => $"{GetType().Name}: {Message}";
}

/// <summary>
/// A failure that is expected to go away on its own — the same request can be
/// retried, with backoff.
/// </summary>
/// <param name="Message">What went wrong.</param>
public abstract record TransientError(string Message) : OcctooError(Message);

/// <summary>
/// The endpoint could not be reached at all: DNS, connection, or TLS failure.
/// </summary>
/// <param name="Message">What went wrong.</param>
public sealed record NetworkError(string Message) : TransientError(Message);

/// <summary>
/// The request ran out of time before Occtoo answered.
/// </summary>
/// <param name="Message">What went wrong.</param>
public sealed record TimeoutError(string Message) : TransientError(Message);

/// <summary>
/// Occtoo answered <c>429 Too Many Requests</c> — the tenant's request-rate
/// budget is exhausted.
/// </summary>
/// <param name="Message">What went wrong.</param>
/// <param name="RetryAfter">
/// How long to wait before retrying, when the response carried a
/// <c>Retry-After</c> header. Honor it when present; otherwise back off
/// exponentially.
/// </param>
public sealed record RateLimitError(string Message, Maybe<TimeSpan> RetryAfter) : TransientError(Message);

/// <summary>
/// Occtoo answered with a <c>5xx</c> status — the failure is on the server's
/// side and retrying with backoff is appropriate.
/// </summary>
/// <param name="Message">What went wrong.</param>
/// <param name="StatusCode">The status Occtoo answered with.</param>
public sealed record ServerError(string Message, int StatusCode) : TransientError(Message);

/// <summary>
/// The credential could not be established or was rejected: bad client secret,
/// revoked API key, denied or expired device authorization, or an invalid token.
/// </summary>
/// <param name="Message">What went wrong.</param>
/// <param name="ErrorCode">
/// The OAuth 2.0 <c>error</c> code, such as <c>invalid_client</c>, when the
/// authorization server supplied one.
/// </param>
public sealed record AuthenticationError(string Message, Maybe<string> ErrorCode) : OcctooError(Message)
{
    /// <summary>Creates an authentication error with no OAuth error code.</summary>
    /// <param name="message">What went wrong.</param>
    public AuthenticationError(string message)
        : this(message, Maybe<string>.None)
    {
    }
}

/// <summary>
/// The credential is valid but not allowed to do this — for ingest, it lacks the
/// <c>write:sources</c> scope or is not granted the target source.
/// </summary>
/// <param name="Message">What went wrong.</param>
public sealed record ForbiddenError(string Message) : OcctooError(Message);

/// <summary>
/// The addressed resource does not exist — for ingest, the source id cannot be
/// resolved within the tenant.
/// </summary>
/// <param name="Message">What went wrong.</param>
public sealed record NotFoundError(string Message) : OcctooError(Message);

/// <summary>
/// The resource is in a state that rejects the request — for ingest, the source
/// is being purged.
/// </summary>
/// <param name="Message">What went wrong.</param>
public sealed record ConflictError(string Message) : OcctooError(Message);

/// <summary>
/// The request was rejected before processing: a malformed payload, or values
/// that do not match the configured source property types. Occtoo validates
/// all-or-nothing, so nothing was accepted.
/// </summary>
/// <param name="Message">What went wrong.</param>
/// <param name="Failures">Validation messages keyed by request path, as returned by Occtoo.</param>
public sealed record ValidationError(
    string Message,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Failures) : OcctooError(Message)
{
    /// <summary>Creates a validation error with no per-path detail.</summary>
    /// <param name="message">What went wrong.</param>
    public ValidationError(string message)
        : this(message, EmptyFailures)
    {
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyFailures =
        new Dictionary<string, IReadOnlyList<string>>();
}

/// <summary>
/// A response the SDK did not expect and cannot classify — an unknown status
/// code, or a body that does not parse.
/// </summary>
/// <param name="Message">What went wrong.</param>
/// <param name="StatusCode">The HTTP status, when the failure was a response rather than a client-side problem.</param>
public sealed record UnexpectedError(string Message, Maybe<int> StatusCode) : OcctooError(Message)
{
    /// <summary>Creates an unexpected error with no associated HTTP status.</summary>
    /// <param name="message">What went wrong.</param>
    public UnexpectedError(string message)
        : this(message, Maybe<int>.None)
    {
    }
}
