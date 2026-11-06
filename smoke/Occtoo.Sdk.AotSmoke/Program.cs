// Runs the SDK's runtime paths inside a native-AOT, trimmed, ICU-less binary —
// the paths that break silently under AOT if a reflection or dynamic-codegen
// dependency sneaks in: value-object construction, the OAuth token exchange
// (snake_case JSON), FusionCache token storage, ingest request serialization,
// receipt parsing, problem-details parsing, the asset payloads and their
// generated filename regex, one byte transfer, the durable-consumer pull
// and acknowledge round trip, and an image upload and conversion. Everything
// is scripted; no network is involved. Exit code 0 means the SDK works in this
// deployment shape.

using System.Net;
using System.Text;
using Occtoo;
using Occtoo.Assets;
using Occtoo.Authentication;
using Occtoo.Events;
using Occtoo.Http;
using Occtoo.ImageConversions;
using Occtoo.ManagedTags;
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
    new OcctooClientOptions
    {
        Credential = credential,
        UploadHttpClient = new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan },
    });

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
    && accepted.Value.NewProperties is [{ Type: { HasValue: true, Value: SourcePropertyType.List } }]);
Check("request body carries native JSON types", transport.LastBody is { } body
    && body.Contains("\"value\":100.111")
    && body.Contains("\"value\":true")
    && body.Contains("[\"summer\",\"sale\"]")
    && body.Contains("\"language\":\"en\""));

var rejected = await client.Sources.IngestEntries(SourceId.From("products"), [entry]);
Check("problem details mapped to ValidationError",
    rejected is { IsFailure: true, Error: ValidationError { Failures.Count: 1 } });

// 3. Events: envelope parsing into the typed records (hand-rolled JsonElement
//    mapping, no reflection) and the filter grammar.
transport.Enqueue(HttpStatusCode.OK, """
    {
      "items": [
        {
          "id": "9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3a",
          "type": "source_entry.added",
          "sequence": "0000000000000001",
          "time": "2026-08-01T10:00:00Z",
          "data": { "sourceId": "products", "entryKey": "sku-123", "version": 1 }
        },
        {
          "id": "9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3b",
          "type": "warehouse.opened",
          "sequence": "0000000000000002",
          "data": { "warehouseId": "north" }
        }
      ],
      "after": "0000000000000002",
      "hasMore": false
    }
    """);

var pulled = await client.Events.Pull();
Check("events page parsed into typed records", pulled.IsSuccess
    && pulled.Value.Items is [
        SourceEntryAdded { SourceId.Value: "products", EntryKey.Value: "sku-123" },
        UnknownEvent { Type: "warehouse.opened" }]
    && pulled.Value.Next.HasValue
    && !pulled.Value.HasMore);
Check("event sequence converts to a cursor",
    pulled.IsSuccess && pulled.Value.Items[0].Sequence.AsCursor().Value == "0000000000000001");

var filter = EventFilter
    .OfType<SourceEntryAdded>(e => e.WithSource("products"))
    .ToString();
Check("event filter renders the grammar",
    filter == """type eq "source_entry.added" and sourceId eq "products" """.TrimEnd());

// Managed-tag value content and enum parsing must also survive trimming.
var managedTagEvent = CloudEvent.Parse("""
    { "type":"managed_tag_value.updated", "sequence":"003.00000000000000184467",
      "data": { "managedTagId":"42f3cce5-a2b8-4cf9-9c2e-8c07a2ced339", "key":"blue",
        "value":{"en":"Blue","sv":"Blå"}, "order":2.5, "createdAt":"2026-11-05T09:15:12Z",
        "createdBy":"c157d71e-b930-4eae-9c3d-23a8de1a1cbc", "valueTruncated":false } }
    """);
Check("managed tag translations parsed without reflection",
    managedTagEvent.IsSuccess && managedTagEvent.Value is ManagedTagValueUpdated
    { Content.HasValue: true, Content.Value: ManagedTagValueContent.LocalizedValue localized }
    && localized.Translations["sv"] == "Blå");
var managedTagListEvent = CloudEvent.Parse("""
    { "type":"managed_tag_list.created", "sequence":"003.00000000000000184468",
      "data": { "managedTagId":"42f3cce5-a2b8-4cf9-9c2e-8c07a2ced339", "displayName":"Colours",
        "type":"LocalizedText", "createdAt":"2026-11-05T09:15:12Z", "lastModifiedAt":"2026-11-05T09:15:12Z" } }
    """);
Check("managed tag list kind parsed under AOT",
    managedTagListEvent.IsSuccess && managedTagListEvent.Value is ManagedTagListCreated
    { TagType.HasValue: true, TagType.Value: ManagedTagType.LocalizedText });
Check("managed tag family filters render under AOT",
    EventFilter.OfType<ManagedTagValueEvent>(e => e.WithManagedTag(
            ManagedTagId.From(Guid.Parse("42f3cce5-a2b8-4cf9-9c2e-8c07a2ced339"))).WithSubject("blue"))
        .ToString() == """(type eq "managed_tag_value.added" or type eq "managed_tag_value.deleted" or type eq "managed_tag_value.updated") and managedTagId eq "42f3cce5-a2b8-4cf9-9c2e-8c07a2ced339" and subject eq "blue" """.TrimEnd());

// 4. Destinations: standalone parsing and webhook signature verification.
const string webhookBody = """
    {
      "id": "9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3c",
      "type": "source_entry.deleted",
      "sequence": "0000000000000099",
      "data": { "sourceId": "products", "entryKey": "sku-9", "version": 3 }
    }
    """;
Check("standalone CloudEvent.Parse", CloudEvent.Parse(webhookBody) is
{ IsSuccess: true, Value: SourceEntryDeleted { EntryKey.Value: "sku-9" } });

const string signingSecret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
var signedAt = DateTimeOffset.UtcNow;
var unixSeconds = signedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
var signature = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(
    Convert.FromBase64String(signingSecret["whsec_".Length..]),
    Encoding.UTF8.GetBytes($"delivery-1.{unixSeconds}.{webhookBody}")));
var verified = OcctooWebhook.Verify(
    "delivery-1",
    unixSeconds,
    $"v1,{signature}",
    Encoding.UTF8.GetBytes(webhookBody),
    signingSecret);
Check("webhook signature verified and event parsed", verified is
{ IsSuccess: true, Value: { Id: "delivery-1", Event: SourceEntryDeleted } });

// 5. Assets: the value objects, the asset payloads, and one byte transfer
Check("AssetKey rejects a key the API would reject", !AssetKey.TryFrom("note.txt", out _));

// The link expiry is relative: a fixed date would slip into the past and then
// send Upload looking for a re-sign the scripted queue has no answer for.
var linkExpiresAt = DateTimeOffset.UtcNow
    .AddHours(1)
    .ToString("O", System.Globalization.CultureInfo.InvariantCulture);

transport.Enqueue(HttpStatusCode.OK, $$"""
    {
      "initialized": {
        "logo": {
          "uploadUrl": "https://tenant.blob.core.windows.net/media/logo.png?sig=abc",
          "expiresAt": "{{linkExpiresAt}}"
        }
      },
      "failed": { "note_1": "Asset already completed" }
    }
    """);

var asset = new Asset(AssetKey.From("logo"), AssetFilename.From("logo.png"));
var initialized = await client.Assets.Initialize(SourceId.From("media"), [asset]);
Check("initialize response parsed", initialized is
{
    IsSuccess: true,
    Value: { Succeeded.Count: 1, Failed.Count: 1 },
});

transport.Enqueue(HttpStatusCode.Created, "");
var link = initialized.Value.Succeeded[AssetKey.From("logo")];
var transferred = await client.Assets.Transfer(link, AssetContent.FromBytes("12345"u8.ToArray()));
Check("bytes transferred against the signed URL", transferred is { IsSuccess: true, Value.Bytes: 5 });
Check("the upload is a block blob with no Occtoo credential",
    transport.LastHeaders.TryGetValue("x-ms-blob-type", out var blobType)
    && blobType == "BlockBlob"
    && !transport.LastHeaders.ContainsKey("Authorization")
    && !transport.LastHeaders.ContainsKey("x-api-key"));

transport.Enqueue(HttpStatusCode.OK, """
    {
      "completed": {
        "logo": {
          "filename": "logo.png",
          "mimeType": "image/png",
          "size": 5,
          "publicUrl": "https://cdn.occtoo.com/media/logo.png",
          "width": 64
        }
      },
      "failed": {}
    }
    """);

var completed = await client.Assets.Complete(SourceId.From("media"), [asset]);
Check("completion response parsed", completed is { IsSuccess: true }
    && completed.Value.Succeeded[AssetKey.From("logo")] is
    { MimeType: "image/png", Size: 5, Width.HasValue: true, Height.HasValue: false });

transport.Enqueue(HttpStatusCode.OK, """
    {
      "logo": {
        "status": "Quarantined",
        "filename": "logo.png",
        "mimeType": "image/png",
        "size": 5,
        "publicUrl": "https://cdn.occtoo.com/media/logo.png"
      }
    }
    """);

var state = await client.Assets.GetState(SourceId.From("media"), [AssetKey.From("logo")]);
Check("asset state parsed, and a status this SDK predates reads as absent",
    state is { IsSuccess: true }
    && state.Value[AssetKey.From("logo")] is
    { Status.HasValue: false, Filename.HasValue: true, Size.HasValue: true });

transport.Enqueue(HttpStatusCode.BadRequest, """
    {
      "title": "Bad Request",
      "status": 400,
      "detail": "Asset uploads can only target Media data sources"
    }
    """);

var refused = await client.Assets.Initialize(SourceId.From("media"), [asset]);
Check("problem details mapped to a ValidationError",
    refused is { IsFailure: true, Error: ValidationError }
    && refused.Error.Message.Contains("Media data sources", StringComparison.Ordinal));

// 6. Durable consumers: the leased batch (source-generated DTO over raw
//    envelopes, then the typed records), the caught-up 204, and the
//    acknowledgement round trip (request body, lenient status enum).
var destinationId = EventDestinationId.From(Guid.Parse("5b0e2f4a-9c1d-4e8b-a7f3-6d2c1b0a9e8f"));
transport.Enqueue(HttpStatusCode.OK, """
    {
      "leaseId": "0199a0c4-3c5e-7d8f-9a1b-2c3d4e5f6a7b",
      "generation": 2,
      "attempt": 3,
      "events": [
        {
          "id": "9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3d",
          "type": "source_entry.updated",
          "sequence": "003.00000000000000184467",
          "data": { "sourceId": "products", "entryKey": "sku-123", "version": 4 }
        }
      ]
    }
    """);
transport.Enqueue(HttpStatusCode.NoContent, "");
transport.Enqueue(HttpStatusCode.OK, """{ "status": "committed", "committed": "003.00000000000000184467" }""");

var leased = await client.Events.Batch.Pull(destinationId, "smoke-worker");
Check("leased batch parsed into typed records", leased is
{
    IsSuccess: true,
    Value: { HasValue: true, Value: { Attempt: 3, Lease.Generation: 2, Events: [SourceEntryUpdated { EntryKey.Value: "sku-123" }] } },
});

var caughtUp = await client.Events.Batch.Pull(destinationId, "smoke-worker");
Check("caught-up pull is an empty result", caughtUp is { IsSuccess: true, Value.HasNoValue: true });

var acknowledged = await client.Events.Batch.Acknowledge(
    new EventBatchLease(destinationId, Guid.Parse("0199a0c4-3c5e-7d8f-9a1b-2c3d4e5f6a7b"), Generation: 2),
    EventBatchOutcome.Ok);
Check("acknowledgement body carries the lease and status", transport.LastBody is { } ack
    && ack.Contains("\"leaseId\":\"0199a0c4-3c5e-7d8f-9a1b-2c3d4e5f6a7b\"")
    && ack.Contains("\"generation\":2")
    && ack.Contains("\"status\":\"ok\""));
Check("acknowledgement parsed with its status and committed position", acknowledged is
{
    IsSuccess: true,
    Value: { Status: { HasValue: true, Value: EventBatchAcknowledgementStatus.Committed }, Committed.Value: "003.00000000000000184467" },
});

// 7. Image conversions: the value objects' generated patterns, the create
//    response, one byte transfer, and the convert payloads with their enums.
Check("ImageAspectRatio rejects a ratio the API would reject", !ImageAspectRatio.TryFrom("01:1", out _));
Check("ImageBackground rejects a color the API would reject", !ImageBackground.TryFrom("#FFFFFF", out _));

transport.Enqueue(HttpStatusCode.Created, """
    {
      "id": "3f2b6c1e-8a4d-4c7b-9e2f-1a5b6c7d8e9f",
      "uploadUrl": "https://tenant.blob.core.windows.net/conversions/shoe.tif?sig=abc",
      "uploadExpiresAt": "2099-01-01T00:00:00Z",
      "maxFileSize": 33554432
    }
    """);
transport.Enqueue(HttpStatusCode.Created, "");

var uploaded = await client.ImageConversions.Upload(AssetContent.FromBytes("12345"u8.ToArray()), "shoe.tif");
Check("image conversion created and its id parsed", uploaded is { IsSuccess: true }
    && uploaded.Value.Value == Guid.Parse("3f2b6c1e-8a4d-4c7b-9e2f-1a5b6c7d8e9f"));
Check("the image upload is a block blob with no Occtoo credential",
    transport.LastHeaders.TryGetValue("x-ms-blob-type", out var imageBlobType)
    && imageBlobType == "BlockBlob"
    && !transport.LastHeaders.ContainsKey("Authorization")
    && !transport.LastHeaders.ContainsKey("x-api-key"));

transport.Enqueue(HttpStatusCode.OK, """
    {
      "url": "https://tenant.blob.core.windows.net/conversions/shoe.webp?sig=def",
      "expiresAt": "2099-01-01T01:00:00Z",
      "filename": "shoe.webp",
      "contentType": "image/webp",
      "size": 81234
    }
    """);

var converted = await client.ImageConversions.Convert(
    uploaded.Value,
    new ImageConversionParameters(ImageConversionMode.FillBackground, 2000, "4:3", ImageOutputFormat.Webp, 80)
    {
        PaddingPercent = 5,
        Background = ImageBackground.From("FFFFFF"),
    });
Check("convert body carries the enum names and the value objects as strings", transport.LastBody is { } convert
    && convert.Contains("\"mode\":\"FillBackground\"")
    && convert.Contains("\"outputFormat\":\"Webp\"")
    && convert.Contains("\"aspectRatio\":\"4:3\"")
    && convert.Contains("\"background\":\"FFFFFF\""));
Check("converted image parsed", converted is
{
    IsSuccess: true,
    Value: { Filename: "shoe.webp", ContentType: "image/webp", Size: 81234 },
} && converted.Value.Url.Host == "tenant.blob.core.windows.net");

Console.WriteLine(failures == 0 ? "AOT smoke: OK" : $"AOT smoke: {failures} FAILED");
return failures == 0 ? 0 : 1;

internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Json)> _responses = new();

    public string? LastBody { get; private set; }

    public Dictionary<string, string> LastHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Enqueue(HttpStatusCode status, string json) => _responses.Enqueue((status, json));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        LastHeaders.Clear();
        foreach (var header in request.Headers)
            LastHeaders[header.Key] = string.Join(",", header.Value);

        var (status, json) = _responses.Dequeue();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
