using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>
/// A verified webhook delivery: the delivery id (stable across retries — use
/// it for idempotency), when it was signed, and the event itself.
/// </summary>
public sealed record WebhookDelivery(string Id, DateTimeOffset SignedAt, CloudEvent Event);

/// <summary>
/// Verifies and parses webhook deliveries from an Occtoo event destination.
/// </summary>
/// <remarks>
/// <para>
/// Deliveries are signed following the Standard Webhooks convention: three
/// headers (<c>webhook-id</c>, <c>webhook-timestamp</c>,
/// <c>webhook-signature</c>) and an HMAC-SHA256 over
/// <c>{id}.{timestamp}.{body}</c> with the destination's <c>whsec_</c> signing
/// secret. <see cref="Verify"/> checks the signature in constant time,
/// enforces the replay window, and parses the body into the typed event — the
/// one call a receiver endpoint needs.
/// </para>
/// <para>
/// Verify against the raw request bytes exactly as received: deserializing
/// and re-serializing the body changes it and the signature no longer
/// matches.
/// </para>
/// </remarks>
public static class OcctooWebhook
{
    /// <summary>The header carrying the delivery id.</summary>
    public const string IdHeader = "webhook-id";

    /// <summary>The header carrying the signing time, as unix seconds.</summary>
    public const string TimestampHeader = "webhook-timestamp";

    /// <summary>The header carrying the <c>v1,&lt;base64&gt;</c> signature.</summary>
    public const string SignatureHeader = "webhook-signature";

    /// <summary>How far a delivery's signing time may lie from now, in either direction.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Verifies a webhook delivery and parses its event.
    /// </summary>
    /// <param name="id">The <c>webhook-id</c> header value.</param>
    /// <param name="timestamp">The <c>webhook-timestamp</c> header value.</param>
    /// <param name="signature">The <c>webhook-signature</c> header value.</param>
    /// <param name="body">The raw request body, exactly as received.</param>
    /// <param name="signingSecret">The destination's <c>whsec_</c> signing secret.</param>
    /// <param name="tolerance">The replay window; <see cref="DefaultTolerance"/> when omitted.</param>
    /// <param name="now">Overrides the clock, for tests.</param>
    /// <returns>
    /// The verified delivery — or an <see cref="AuthenticationError"/> when
    /// the request is not authentic (answer <c>401</c>), a
    /// <see cref="ValidationError"/> when the secret is malformed or the body
    /// is not a CloudEvent (answer <c>400</c>).
    /// </returns>
    public static Result<WebhookDelivery, OcctooError> Verify(
        string? id,
        string? timestamp,
        string? signature,
        ReadOnlySpan<byte> body,
        string signingSecret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        if (!TryReadSecret(signingSecret, out var secret))
            return new ValidationError("The signing secret must be the whsec_ value returned when the destination was created.");

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
            return new AuthenticationError("One or more webhook signature headers are missing.");

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return new AuthenticationError("The webhook timestamp is invalid.");

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new AuthenticationError("The webhook timestamp is outside the supported range.");
        }

        if (((now ?? DateTimeOffset.UtcNow) - signedAt).Duration() > (tolerance ?? DefaultTolerance))
            return new AuthenticationError("The webhook timestamp is outside the accepted replay window.");

        var prefix = Encoding.UTF8.GetBytes($"{id}.{timestamp}.");
        var signedContent = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signedContent, 0);
        body.CopyTo(signedContent.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(secret, signedContent);

        if (!AnySignatureMatches(signature, expected))
            return new AuthenticationError("The webhook signature does not match the request body.");

        return CloudEvent.Parse(body.ToArray())
            .Map(evt => new WebhookDelivery(id, signedAt, evt));
    }

    /// <summary>
    /// The header may carry several space-separated signatures during secret
    /// rotation; the delivery is authentic when any <c>v1</c> signature
    /// matches.
    /// </summary>
    private static bool AnySignatureMatches(string header, byte[] expected)
    {
        foreach (var candidate in header.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "v1,";
            if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var buffer = new byte[expected.Length];
            if (Convert.TryFromBase64String(candidate[prefix.Length..], buffer, out var written)
                && written == expected.Length
                && CryptographicOperations.FixedTimeEquals(buffer, expected))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadSecret(string value, out byte[] secret)
    {
        const string prefix = "whsec_";
        secret = [];
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        try
        {
            secret = Convert.FromBase64String(value[prefix.Length..]);
            return secret.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
