using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>
/// The handle to one leased batch — what
/// <see cref="EventBatchClient.Acknowledge"/> settles. It carries the
/// destination it was leased from, so an acknowledgement cannot land on the
/// wrong durable consumer.
/// </summary>
/// <remarks>
/// A worker that hands processing to another process can persist the three
/// values and rebuild the lease there; the acknowledgement needs nothing else.
/// </remarks>
/// <param name="DestinationId">The durable consumer the batch was leased from.</param>
/// <param name="LeaseId">The lease — the same across every redelivery of the batch.</param>
/// <param name="Generation">
/// Which handout of the lease this is. Every redelivery raises it, and an
/// acknowledgement for an older generation is
/// <see cref="EventBatchAcknowledgementStatus.Stale"/>: the batch belongs to
/// whoever holds the newest handout.
/// </param>
public sealed record EventBatchLease(EventDestinationId DestinationId, Guid LeaseId, int Generation);

/// <summary>
/// One leased batch from a durable consumer: its events, in stream order, and
/// the lease to acknowledge them with.
/// </summary>
/// <param name="Lease">Pass to <see cref="EventBatchClient.Acknowledge"/> once the events are processed.</param>
/// <param name="Attempt">
/// How often the batch has been handed out: <c>1</c> on first delivery, higher
/// once a lease expired, was failed, or was abandoned. Delivery is
/// at-least-once, so a batch past its first attempt may already be partly
/// processed — and a batch that keeps coming back is the signal to park it
/// rather than fail it forever.
/// </param>
/// <param name="Events">
/// The leased events, in ascending sequence order. It can be empty — a
/// redelivered range whose events aged out of retention — and still needs
/// acknowledging so the position moves past it.
/// </param>
public sealed record EventBatch(EventBatchLease Lease, int Attempt, IReadOnlyList<CloudEvent> Events);

/// <summary>
/// How much to lease on a <see cref="EventBatchClient.Pull"/>, and for how long.
/// </summary>
public sealed record EventBatchOptions
{
    /// <summary>The API's ceiling for <see cref="Limit"/>.</summary>
    public const int MaxLimit = 100;

    /// <summary>The API's ceiling for <see cref="LeaseDuration"/>: ten minutes.</summary>
    public static readonly TimeSpan MaxLeaseDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Maximum events per batch, between 1 and 100. Defaults to 20. The API may
    /// return fewer — a batch never takes the durable consumer past its window
    /// of unacknowledged events.
    /// </summary>
    public int Limit { get; init; } = 20;

    /// <summary>
    /// How long the batch stays leased to this worker before it is handed to
    /// another — the API's <c>visibility</c>. Between one second and ten
    /// minutes, sent in whole seconds (rounded up); defaults to 60 seconds.
    /// Size it above the worst-case processing time of a batch: an
    /// acknowledgement that arrives after the lease expired is
    /// <see cref="EventBatchAcknowledgementStatus.Stale"/> and commits nothing.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(60);

    internal UnitResult<OcctooError> Validate()
    {
        if (Limit is < 1 or > MaxLimit)
            return new ValidationError($"{nameof(Limit)} must be between 1 and {MaxLimit}.");

        if (LeaseDuration < TimeSpan.FromSeconds(1) || LeaseDuration > MaxLeaseDuration)
            return new ValidationError($"{nameof(LeaseDuration)} must be between one second and {MaxLeaseDuration.TotalMinutes:0} minutes.");

        return UnitResult.Success<OcctooError>();
    }

    internal int LeaseSeconds => (int)Math.Ceiling(LeaseDuration.TotalSeconds);
}

/// <summary>
/// How a worker settles a leased batch.
/// </summary>
public enum EventBatchOutcome
{
    /// <summary>
    /// Every event was processed: the batch is committed, and the durable
    /// consumer's position moves past it once everything before it is too.
    /// </summary>
    Ok,

    /// <summary>
    /// Processing failed: the batch is requeued at once and redelivered — to
    /// any worker — with a higher <see cref="EventBatch.Attempt"/>.
    /// </summary>
    Failed,
}

/// <summary>
/// What an acknowledgement did.
/// </summary>
public enum EventBatchAcknowledgementStatus
{
    /// <summary>The batch was committed (acknowledged <see cref="EventBatchOutcome.Ok"/>).</summary>
    Committed,

    /// <summary>The batch was requeued for redelivery (acknowledged <see cref="EventBatchOutcome.Failed"/>).</summary>
    Requeued,

    /// <summary>
    /// This acknowledgement changed nothing: the lease expired and was handed
    /// out again, or the batch was already acknowledged — possibly by this
    /// acknowledgement's own earlier attempt, if its response was lost. The
    /// worker no longer owns the batch — not an error, but a lease that
    /// routinely goes stale is shorter than the processing it covers; raise
    /// <see cref="EventBatchOptions.LeaseDuration"/>.
    /// </summary>
    Stale,
}

/// <summary>
/// The result of acknowledging a leased batch.
/// </summary>
/// <param name="Status">
/// What the acknowledgement did — absent for a status this SDK version does
/// not know.
/// </param>
/// <param name="Committed">
/// The durable consumer's committed position after the acknowledgement: every
/// event up to and including it is acknowledged. Batches commit as a
/// contiguous prefix, so the position stays behind an older batch another
/// worker still holds.
/// </param>
public sealed record EventBatchAcknowledgement(
    Maybe<EventBatchAcknowledgementStatus> Status,
    EventSequence Committed);
