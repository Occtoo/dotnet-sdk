// Runs the SDK's runtime paths inside a native-AOT, trimmed, ICU-less binary —
// the paths that break silently under AOT if a reflection or dynamic-codegen
// dependency sneaks in: value-object construction, the OAuth token exchange
// (snake_case JSON), FusionCache token storage, ingest request serialization,
// receipt parsing, and problem-details parsing. Everything is scripted; no
// network is involved. Exit code 0 means the SDK works in this deployment shape.

using System.Net;
using System.Text;
using Occtoo;
using Occtoo.Authentication;
using Occtoo.Http;
using Occtoo.Sources;

var failures = 0;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

// 1. Value objects: validation, normalization, implicit conversions. The ISO
//    language check must hold without ICU — that is why it is hand-rolled.
PropertyId propertyId = "PublishedAt";
Check("PropertyId lowercased", propertyId.Value == "publishedat");
LanguageCode language = "sv-se";
Check("LanguageCode ISO-normalized without ICU", language.Value == "sv-SE");
Check("LanguageCode rejects garbage", !LanguageCode.TryFrom("not-a-language", out _));

// 2. Token exchange, ingest round trip, and error mapping over scripted HTTP.
var transport = new ScriptedHandler();
transport.Enqueue(HttpStatusCode.OK, """{"access_token":"token-1","expires_in":3600}""");
transport.Enqueue(HttpStatusCode.Accepted, """
    {
      "correlationId": "83b538a7-df7c-4cf4-b988-cd3b71c4cd90",
      "sourceId": "products",
      "acceptedAt": "2026-08-13T10:15:30Z",
      "acceptedEntryCount": 1,
      "newPropertiesFound": [ { "id": "tags", "type": "List", "delimiter": "," } ]
    }
    """);
transport.Enqueue(HttpStatusCode.BadRequest, """
    {
      "title": "One or more validation errors occurred.",
      "status": 400,
      "traceId": "00-abc-def-01",
      "errors": { "entries[0].properties[0].value": [ "bad" ] }
    }
    """);

using var credential = OcctooCredential.ClientCredentials(
    new OcctooAuthorityOptions
    {
        ClientId = ClientId.From("client-abc"),
        Audience = Audience.From("tenant-1"),
        Scopes = [OcctooScopes.WriteSources],
    },
    ClientSecret.From("secret"),
    new HttpClient(transport));

using var client = new OcctooClient(
    new HttpClient(new OcctooAuthenticationHandler(credential) { InnerHandler = transport }),
    new OcctooClientOptions { Credential = credential });

var authenticated = await client.Authenticate();
Check("token exchange parsed and cached", authenticated.IsSuccess
    && authenticated.Value.HasValue
    && authenticated.Value.Value.Value == "token-1");

var entry = SourceEntry.WithId("sku-123")
    .WithLocalizedText("name", "Blue chair", "en")
    .WithDecimal("price", 100.111m)
    .WithBoolean("inStock", true)
    .WithTimestamp("publishedAt", DateTimeOffset.Parse("2026-01-01T00:00:00Z", null))
    .WithList("tags", "summer", "sale");

var accepted = await client.Sources.IngestEntries(SourceId.From("products"), [entry]);
Check("ingest receipt parsed", accepted.IsSuccess
    && accepted.Value.AcceptedEntryCount == 1
    && accepted.Value.NewProperties is [{ Type: SourcePropertyType.List }]);
Check("request body carries native JSON types", transport.LastBody is { } body
    && body.Contains("\"value\":100.111")
    && body.Contains("\"value\":true")
    && body.Contains("[\"summer\",\"sale\"]")
    && body.Contains("\"language\":\"en\""));

var rejected = await client.Sources.IngestEntries(SourceId.From("products"), [entry]);
Check("problem details mapped to ValidationError",
    rejected is { IsFailure: true, Error: ValidationError { Failures.Count: 1 } });

Console.WriteLine(failures == 0 ? "AOT smoke: OK" : $"AOT smoke: {failures} FAILED");
return failures == 0 ? 0 : 1;

internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Json)> _responses = new();

    public string? LastBody { get; private set; }

    public void Enqueue(HttpStatusCode status, string json) => _responses.Enqueue((status, json));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var (status, json) = _responses.Dequeue();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
