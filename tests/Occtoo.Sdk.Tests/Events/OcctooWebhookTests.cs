using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Occtoo.Events;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class OcctooWebhookTests
{
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-23T12:00:00Z", null);

    private const string Body = """
        {
          "specversion": "1.0",
          "id": "9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3a",
          "type": "source_entry.added",
          "sequence": "003.00000000000000184001",
          "subject": "sku-123",
          "data": { "sourceId": "products", "entryKey": "sku-123", "version": 1 }
        }
        """;

    private static string Sign(string id, DateTimeOffset timestamp, string body, string secret = Secret)
    {
        var key = Convert.FromBase64String(secret["whsec_".Length..]);
        var signature = HMACSHA256.HashData(
            key, Encoding.UTF8.GetBytes($"{id}.{UnixSeconds(timestamp)}.{body}"));
        return $"v1,{Convert.ToBase64String(signature)}";
    }

    private static string UnixSeconds(DateTimeOffset timestamp) =>
        timestamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Verifies_a_signed_delivery_and_parses_the_typed_event()
    {
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(Now),
            Sign("delivery-1", Now, Body),
            Encoding.UTF8.GetBytes(Body),
            Secret,
            now: Now);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        var delivery = result.Value;
        delivery.Id.ShouldBe("delivery-1");
        delivery.SignedAt.ShouldBe(Now);
        delivery.Event.ShouldBeOfType<SourceEntryAdded>().EntryKey.Value.ShouldBe("sku-123");
    }

    [Fact]
    public void Rejects_a_tampered_body()
    {
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(Now),
            Sign("delivery-1", Now, Body),
            Encoding.UTF8.GetBytes(Body.Replace("sku-123", "sku-666")),
            Secret,
            now: Now);

        result.Error.ShouldBeOfType<AuthenticationError>()
            .Message.ShouldContain("does not match");
    }

    [Fact]
    public void Rejects_a_signature_made_with_another_secret()
    {
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(Now),
            Sign("delivery-1", Now, Body, secret: "whsec_b3RoZXItc2VjcmV0LW90aGVyLXNlY3JldA=="),
            Encoding.UTF8.GetBytes(Body),
            Secret,
            now: Now);

        result.Error.ShouldBeOfType<AuthenticationError>();
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(10)]
    public void Rejects_a_timestamp_outside_the_replay_window_in_both_directions(int minutes)
    {
        var signedAt = Now.AddMinutes(minutes);
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(signedAt),
            Sign("delivery-1", signedAt, Body),
            Encoding.UTF8.GetBytes(Body),
            Secret,
            now: Now);

        result.Error.ShouldBeOfType<AuthenticationError>()
            .Message.ShouldContain("replay window");
    }

    [Fact]
    public void A_wider_tolerance_admits_an_older_delivery()
    {
        var signedAt = Now.AddMinutes(-10);
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(signedAt),
            Sign("delivery-1", signedAt, Body),
            Encoding.UTF8.GetBytes(Body),
            Secret,
            tolerance: TimeSpan.FromMinutes(15),
            now: Now);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Accepts_any_matching_signature_during_secret_rotation()
    {
        var stale = Sign("delivery-1", Now, Body, secret: "whsec_b3RoZXItc2VjcmV0LW90aGVyLXNlY3JldA==");
        var current = Sign("delivery-1", Now, Body);

        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(Now),
            $"{stale} {current}",
            Encoding.UTF8.GetBytes(Body),
            Secret,
            now: Now);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "1755950400", "v1,sig")]
    [InlineData("delivery-1", null, "v1,sig")]
    [InlineData("delivery-1", "1755950400", null)]
    public void Rejects_missing_headers(string? id, string? timestamp, string? signature)
    {
        var result = OcctooWebhook.Verify(
            id, timestamp, signature, Encoding.UTF8.GetBytes(Body), Secret, now: Now);

        result.Error.ShouldBeOfType<AuthenticationError>().Message.ShouldContain("missing");
    }

    [Fact]
    public void Rejects_a_signature_that_is_not_v1()
    {
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(Now),
            "v2,AAAA",
            Encoding.UTF8.GetBytes(Body),
            Secret,
            now: Now);

        result.Error.ShouldBeOfType<AuthenticationError>();
    }

    [Fact]
    public void A_malformed_signing_secret_is_the_callers_mistake()
    {
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(Now),
            Sign("delivery-1", Now, Body),
            Encoding.UTF8.GetBytes(Body),
            "not-a-whsec-value",
            now: Now);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("whsec_");
    }

    [Fact]
    public void An_authentic_delivery_whose_body_is_not_a_cloud_event_is_a_validation_error()
    {
        const string body = "not json at all";
        var result = OcctooWebhook.Verify(
            "delivery-1",
            UnixSeconds(Now),
            Sign("delivery-1", Now, body),
            Encoding.UTF8.GetBytes(body),
            Secret,
            now: Now);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("not valid JSON");
    }

    // ── CloudEvent.Parse — the queue-consumption path ───────────────────────

    [Fact]
    public void Parse_reads_a_delivery_from_any_destination_into_the_typed_records()
    {
        CloudEvent.Parse(Body).Value.ShouldBeOfType<SourceEntryAdded>();
        CloudEvent.Parse(Encoding.UTF8.GetBytes(Body).AsMemory()).Value.ShouldBeOfType<SourceEntryAdded>();

        using var document = JsonDocument.Parse(Body);
        CloudEvent.Parse(document.RootElement).Value.ShouldBeOfType<SourceEntryAdded>();
    }

    [Fact]
    public void Parse_returns_a_validation_error_for_a_body_that_is_not_json()
    {
        CloudEvent.Parse("{{{").Error.ShouldBeOfType<ValidationError>();
    }
}
