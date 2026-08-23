using CSharpFunctionalExtensions;

namespace Occtoo.Authentication;

/// <summary>
/// Authenticates with an Occtoo organization API key, sent as <c>x-api-key</c>.
/// </summary>
/// <remarks>
/// The simplest credential: no token exchange, no expiry, no round trip before
/// the first call. Its permissions are fixed to those granted to the key, and it
/// cannot be scoped to an individual user — prefer
/// <see cref="OcctooCredential.ClientCredentials"/> for server integrations that
/// need rotation, and a user flow when actions should be attributable.
/// </remarks>
internal sealed class ApiKeyCredential(ApiKey apiKey) : IOcctooCredential
{
    /// <summary>The header Occtoo reads the API key from.</summary>
    internal const string HeaderName = "x-api-key";

    /// <inheritdoc />
    public ValueTask<UnitResult<OcctooError>> Apply(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ValueTask.FromResult(
                UnitResult.Failure<OcctooError>(new ValidationError("A request is required.")));
        }

        // Replace rather than add: a retried request already carries the header.
        request.Headers.Remove(HeaderName);
        request.Headers.Add(HeaderName, apiKey.Value);
        return ValueTask.FromResult(UnitResult.Success<OcctooError>());
    }
}
