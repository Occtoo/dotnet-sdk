using CSharpFunctionalExtensions;

namespace Occtoo.Authentication;

/// <summary>
/// Supplies the credentials for an Occtoo API request.
/// </summary>
/// <remarks>
/// <para>
/// Occtoo accepts two kinds of credential — an organization API key in
/// <c>x-api-key</c>, and a bearer token from the Occtoo authorization server.
/// This interface covers both, because each one ultimately just decorates the
/// outgoing request.
/// </para>
/// <para>
/// Use <see cref="OcctooCredential"/> to build the credential you need. Implement
/// this interface directly only when Occtoo's own flows do not fit — for example
/// when a token already exists in an ambient context and must be read per
/// request. Implementations must be thread-safe and are expected to be reused
/// for the lifetime of a client.
/// </para>
/// </remarks>
public interface IOcctooCredential
{
    /// <summary>
    /// Adds this credential to <paramref name="request"/>, acquiring or
    /// refreshing a token first if the credential needs one.
    /// </summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <returns>
    /// Success once the request carries the credential, or the
    /// <see cref="OcctooError"/> that prevented establishing it — typically an
    /// <see cref="AuthenticationError"/> or a <see cref="TransientError"/> from
    /// reaching the authorization server.
    /// </returns>
    ValueTask<UnitResult<OcctooError>> Apply(HttpRequestMessage request, CancellationToken cancellationToken);
}
