// Calls a protected destination API with a tenant application.
//
// The application needs a grant for the API version — add it in Studio, or with
//   CreateApplication.WithName("Webshop reader").WithApiVersions(apiVersionId)
// (WithDestinations covers every version of a destination). Its client id and
// secret then reach every Occtoo API it is granted; only the audience changes.
//
// Fill in the placeholders and `dotnet run`.

using Occtoo.Authentication;
using Occtoo.Http;

// ── Configuration ──────────────────────────────────────────────────────────
const string OcctooClientId = "<your-application-client-id>";
const string OcctooClientSecret = "<your-application-client-secret>";
const string ApiVersionId = "<your-api-version-id>";              // listed in the application's audiences
const string DestinationEndpoint = "<your-destination-endpoint-url>"; // from the destination's API documentation
// ───────────────────────────────────────────────────────────────────────────

// The destination gateway accepts a token only when its audience is the API
// version id, so request exactly that — and no scopes: a destination checks the
// audience, and a tenant-audience token is rejected.
using var credential = OcctooCredential.ClientCredentials(
    new OcctooAuthorityOptions
    {
        ClientId = ClientId.From(OcctooClientId),
        Audience = Audience.From(ApiVersionId),
    },
    ClientSecret.From(OcctooClientSecret));

// The SDK's handler fetches, caches and renews the token, and attaches it to
// every request this client sends.
using var http = new HttpClient(new OcctooAuthenticationHandler(credential)
{
    InnerHandler = new SocketsHttpHandler(),
});

try
{
    using var response = await http.GetAsync(new Uri(DestinationEndpoint));
    Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
    Console.WriteLine(await response.Content.ReadAsStringAsync());
    return response.IsSuccessStatusCode ? 0 : 1;
}
catch (OcctooCredentialException exception)
{
    // A handler has no result channel, so a rejected credential arrives as an
    // exception carrying the typed error.
    Console.Error.WriteLine($"Token request failed: {exception.Error}");
    return 1;
}
