using CSharpFunctionalExtensions;
using Occtoo.Sources;

namespace Occtoo.Assets;

/// <summary>
/// How one asset fared in an upload run.
/// </summary>
/// <remarks>
/// The outcome is the same <c>Result&lt;T, OcctooError&gt;</c> the rest of the
/// SDK returns, so a <see cref="TransientError"/> still means this one asset is
/// worth another run. <see cref="Reached"/> says where it stopped.
/// </remarks>
/// <param name="Key">The asset.</param>
/// <param name="Filename">The name the file is stored under.</param>
/// <param name="Reached">How far it got.</param>
/// <param name="Outcome">The file that now exists, or why it does not.</param>
public sealed record AssetUploadOutcome(
    AssetKey Key,
    AssetFilename Filename,
    AssetUploadStage Reached,
    Result<AssetFileInfo, OcctooError> Outcome);

/// <summary>
/// What an upload run did, per asset, in the order the assets were passed in.
/// </summary>
/// <remarks>
/// A returned report means the run itself went through; individual assets may
/// still have failed. A failed <c>Result</c> from
/// <see cref="AssetsClient.Upload"/> means the run could not proceed at all —
/// nothing was uploaded.
/// </remarks>
/// <param name="DataSourceId">The data source the assets were uploaded into.</param>
/// <param name="Outcomes">One outcome per asset, in the caller's order.</param>
public sealed record AssetUploadReport(SourceId DataSourceId, IReadOnlyList<AssetUploadOutcome> Outcomes)
{
    /// <summary>The assets that now have a file.</summary>
    public IReadOnlyList<AssetUploadOutcome> Completed { get; } =
        [.. Outcomes.Where(outcome => outcome.Outcome.IsSuccess)];

    /// <summary>The assets that do not.</summary>
    public IReadOnlyList<AssetUploadOutcome> Failed { get; } =
        [.. Outcomes.Where(outcome => outcome.Outcome.IsFailure)];

    /// <summary>Whether every asset in the run has a file.</summary>
    public bool AllCompleted => Failed.Count == 0;
}
