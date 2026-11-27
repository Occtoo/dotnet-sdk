// Image conversion end to end: upload a small image, queue a conversion,
// poll the job until it's done and download the result.
//
// Fill in the placeholders and `dotnet run` to convert with the parameters
// below. To use one of your tenant's conversion presets instead, pass its id:
// `dotnet run -- web-thumbnail`. Ctrl+C stops.

using Occtoo;
using Occtoo.Assets;
using Occtoo.Authentication;
using Occtoo.ImageConversions;

// ── Configuration ──────────────────────────────────────────────────────────
const string OcctooClientId = "<your-application-client-id>";
const string OcctooClientSecret = "<your-application-client-secret>";
const string OcctooTenantId = "<your-tenant-id>";
// ───────────────────────────────────────────────────────────────────────────

ConversionPresetId? preset = null;
if (args.Length > 0)
{
    if (!ConversionPresetId.TryFrom(args[0], out var parsed))
    {
        Console.Error.WriteLine($"'{args[0]}' isn't a valid conversion preset id.");
        return 1;
    }

    preset = parsed;
}

using var client = new OcctooClient(new OcctooClientOptions
{
    Credential = OcctooCredential.ClientCredentials(
        new OcctooAuthorityOptions
        {
            ClientId = ClientId.From(OcctooClientId),
            Audience = Audience.From(OcctooTenantId),
            Scopes = [OcctooScopes.WriteMedia],
        },
        ClientSecret.From(OcctooClientSecret)),
});

// Ctrl+C cancels the wait between reads and any call in flight.
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, keyPress) =>
{
    keyPress.Cancel = true;
    stopping.Cancel();
};

var thumbnail = new ImageConversionParameters(
    ImageConversionMode.Fit, TargetSize: 400, AspectRatio: "1:1", ImageOutputFormat.Webp, Quality: 80);

try
{
    var uploaded = await client.ImageConversions.Upload(
        AssetContent.FromFile(Path.Combine(AppContext.BaseDirectory, "files", "disc.png")), "disc.png", stopping.Token);
    if (uploaded.IsFailure)
    {
        Console.Error.WriteLine($"Upload failed: {uploaded.Error}");
        return 1;
    }

    var id = uploaded.Value;

    // Convert only queues the conversion and returns the job's id. Keep it:
    // every Convert call queues another conversion, so a resent call would
    // queue a second one.
    var queued = preset is { } presetId
        ? await client.ImageConversions.Convert(id, presetId, stopping.Token)
        : await client.ImageConversions.Convert(id, thumbnail, stopping.Token);
    if (queued.IsFailure)
    {
        Console.Error.WriteLine($"Conversion wasn't queued: {queued.Error}");
        return 1;
    }

    Console.WriteLine($"Conversion {queued.Value.Value} queued. Waiting for it to finish (Ctrl+C to stop).");

    // Convert returns before the conversion runs, so read the job about once
    // a second until it completes or fails.
    ConvertedImage image;
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), stopping.Token);

        var read = await client.ImageConversions.GetJob(id, queued.Value, stopping.Token);
        if (read.IsFailure)
        {
            if (read.Error is TransientError)
            {
                Console.WriteLine($"Couldn't read the conversion, retrying: {read.Error}");
                continue;
            }

            // Reading again won't fix this one: a refused credential, a job
            // Occtoo no longer keeps.
            Console.Error.WriteLine($"Couldn't read the conversion: {read.Error}");
            return 1;
        }

        var job = read.Value;
        if (job.Status == ConversionJobStatus.Completed)
        {
            image = job.Image.Value;
            break;
        }

        if (job.Status == ConversionJobStatus.Failed)
        {
            Console.Error.WriteLine($"Conversion failed: {job.Error.GetValueOrDefault("Occtoo didn't say why.")}");
            return 1;
        }

        // Queued, Running, or a status this SDK version doesn't know: read again.
    }

    // The link is pre-signed and lasts an hour, so a plain HttpClient with no
    // Occtoo credential downloads it. The filename comes from the server, so
    // use only its name part, never a path.
    var path = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(image.Filename));
    try
    {
        using var http = new HttpClient();
        await using var body = await http.GetStreamAsync(image.Url, stopping.Token);
        await using var file = File.Create(path);
        await body.CopyToAsync(file, stopping.Token);
    }
    catch (Exception exception) when (exception is HttpRequestException or IOException
        or TaskCanceledException { InnerException: TimeoutException })
    {
        Console.Error.WriteLine($"Download failed: {exception.Message}");
        return 1;
    }

    Console.WriteLine($"Saved {path} ({new FileInfo(path).Length} bytes).");
}
catch (OperationCanceledException)
{
    // Ctrl+C while uploading, converting, waiting or downloading.
    Console.WriteLine("Stopped.");
}

return 0;
