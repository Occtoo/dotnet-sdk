using System.Diagnostics;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Occtoo;
using Occtoo.Assets;
using Occtoo.Sources;

namespace Occtoo.Sdk.Examples.Assets.Sync;

/// <summary>
/// Turns the source system's file events into Occtoo calls, one call per
/// event: a new file is uploaded, a new version replaces the file, new
/// metadata goes through typed ingest on the asset's key and leaves the file
/// alone, and a deleted file deletes the asset.
/// </summary>
/// <remarks>
/// The SDK already retries transient failures internally, so any error
/// reaching this worker survived those retries. Every failure is logged and
/// the worker moves on to the next event; an event that can't succeed — an
/// asset that already exists, one that's already gone — doesn't stop the rest.
/// </remarks>
internal sealed class SyncWorker(
    OcctooClient client,
    OcctooSettings settings,
    IHostApplicationLifetime lifetime,
    ILogger<SyncWorker> logger) : BackgroundService
{
    private readonly SourceId _source = SourceId.From(settings.SourceId);
    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, settings.Folder);

    // Fail fast: prove the credential and set up the data source before the
    // first event, and stop the host if any of it fails.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        client
            .Authenticate(stoppingToken)
            .Tap(token => logger.LogInformation(
                "Authenticated against {BaseAddress}, token expires {ExpiresOn:u}",
                client.BaseAddress,
                token.HasValue ? token.Value.ExpiresOn : DateTimeOffset.MaxValue))
            .Bind(_ => EnsureMediaSource(stoppingToken))
            .Bind(_ => EnsureProperty("title", "Title", SourcePropertyType.LocalizedText, stoppingToken))
            .Bind(_ => EnsureProperty("tags", "Tags", SourcePropertyType.List, stoppingToken))
            .Bind(_ => EnsureProperty("width_mm", "Width (mm)", SourcePropertyType.Integer, stoppingToken))
            .Bind(_ => EnsureProperty("published", "Published", SourcePropertyType.Boolean, stoppingToken))
            .TapError(error =>
            {
                logger.LogCritical("Can't start syncing: {Error}", error);
                lifetime.StopApplication();
            })
            .Tap(_ => HandleEvents(stoppingToken));

    private async Task HandleEvents(CancellationToken stoppingToken)
    {
        await foreach (var fileEvent in SourceSystem.ReadEvents(_folder, settings.Interval, stoppingToken))
        {
            await Send(fileEvent, stoppingToken).Match(
                outcome => logger.LogInformation("{Event} {Key}: {Outcome}", fileEvent.GetType().Name, fileEvent.Key.Value, outcome),
                error => LogFailure(fileEvent, error));
        }
    }

    private Task<Result<string, OcctooError>> Send(FileEvent fileEvent, CancellationToken stoppingToken) =>
        fileEvent switch
        {
            FileAdded added => client.Assets
                .Upload(_source, [AssetUpload.FromFile(added.Key, added.Path).WithMetadata(added.Metadata)], cancellationToken: stoppingToken)
                .Bind(report => report.Outcomes[0].Outcome)
                .Map(file => $"uploaded, public URL {file.PublicUrl}"),

            FileUpdated updated => client.Assets
                .Replace(_source, [AssetUpload.FromFile(updated.Key, updated.Path).WithMetadata(updated.Metadata)], cancellationToken: stoppingToken)
                .Bind(report => report.Outcomes[0].Outcome)
                .Map(file => $"file replaced, public URL {file.PublicUrl}"),

            FileMetadataUpdated changed => client.Sources
                .IngestEntries(_source, [SourceEntry.WithId(changed.Key.AsEntryId()).WithMetadata(changed.Metadata).Build()], stoppingToken)
                .Map(receipt => $"metadata updated (correlation {receipt.CorrelationId.Value})"),

            FileDeleted deleted => client.Assets
                .Delete(_source, [deleted.Key], stoppingToken)
                .Map(() => "asset deleted"),

            _ => throw new UnreachableException($"The worker doesn't handle {fileEvent.GetType().Name}."),
        };

    private void LogFailure(FileEvent fileEvent, OcctooError error)
    {
        switch (error)
        {
            case TransientError:
                // Still failing after the SDK's own retries. A real queue
                // redelivers an event it never got an acknowledgement for;
                // this stand-in feed just moves on.
                logger.LogWarning("{Event} {Key} failed, would be retried: {Error}", fileEvent.GetType().Name, fileEvent.Key.Value, error);
                break;

            default:
                // An asset that already exists, one that's already gone, a
                // value Occtoo refused: sending it again gets the same answer.
                logger.LogError("{Event} {Key} skipped: {Error}", fileEvent.GetType().Name, fileEvent.Key.Value, error);
                break;
        }
    }

    private Task<Result<Source, OcctooError>> EnsureMediaSource(CancellationToken stoppingToken) =>
        client.Sources
            .Get(_source, stoppingToken)
            .OnFailureCompensate(async error => error is NotFoundError
                ? await client.Sources
                    .Create(new(_source, "SDK assets sync") { Type = SourceType.Media }, stoppingToken)
                    .Tap(created => logger.LogInformation("Created the Media data source '{Source}'", created.Id.Value))
                : error)
            .Ensure(
                source => source.Type == SourceType.Media,
                source => new ValidationError($"'{source.Id.Value}' isn't a Media data source. Set Occtoo:SourceId to another id."));

    private Task<Result<SourceProperty, OcctooError>> EnsureProperty(
        PropertyId id, string displayName, SourcePropertyType type, CancellationToken stoppingToken) =>
        client.Sources
            .GetProperty(_source, id, stoppingToken)
            .OnFailureCompensate(async error => error is NotFoundError
                ? await client.Sources
                    .UpsertProperty(_source, id, new(displayName)
                    {
                        Type = type,
                        Delimiter = type == SourcePropertyType.List ? Delimiter.From(",") : Maybe<Delimiter>.None,
                    }, stoppingToken)
                    .Tap(added => logger.LogInformation("Added the property {Property} ({Type})", added.Id.Value, type))
                : error)
            .Ensure(
                property => property.Type == type,
                property => new ValidationError(
                    $"The property {property.Id.Value} is {property.Type.Map(have => have.ToString()).GetValueOrDefault("untyped")}, " +
                    $"but the worker needs {type}. Delete it or use another data source."));
}

internal static class FileMetadataExtensions
{
    /// <summary>
    /// Writes the file's metadata onto an asset upload or an ingest entry
    /// alike: both use the same builder. A value the source system doesn't
    /// have goes out cleared; on a new asset that's the same as leaving it out.
    /// </summary>
    public static EntryPropertyBuilder<TResult> WithMetadata<TResult>(
        this EntryPropertyBuilder<TResult> builder,
        FileMetadata metadata)
    {
        var described = builder
            .WithLocalizedText("title", metadata.TitleEn, "en")
            .WithList("tags", metadata.Tags)
            .WithBoolean("published", metadata.Published);

        described = metadata.TitleSv is null
            ? described.WithCleared("title", "sv")
            : described.WithLocalizedText("title", metadata.TitleSv, "sv");

        return metadata.WidthMm is null
            ? described.WithCleared("width_mm")
            : described.WithInteger("width_mm", metadata.WidthMm.Value);
    }
}
