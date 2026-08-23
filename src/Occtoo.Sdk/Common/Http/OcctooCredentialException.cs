namespace Occtoo.Http;

/// <summary>
/// Thrown by <see cref="OcctooAuthenticationHandler"/> when the credential could
/// not be established, because a <see cref="DelegatingHandler"/> has no other
/// way to report a failure than an exception.
/// </summary>
/// <remarks>
/// The SDK's own surfaces catch this and return the carried
/// <see cref="OcctooError"/> as a failed result — it reaches user code only when
/// the handler is used in a hand-built <see cref="HttpClient"/> pipeline.
/// </remarks>
public sealed class OcctooCredentialException : Exception
{
    internal OcctooCredentialException(OcctooError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>Why the credential could not be established.</summary>
    public OcctooError Error { get; }
}
