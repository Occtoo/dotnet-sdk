namespace Occtoo;

/// <summary>
/// Thrown by the <c>ListAll</c> enumerables when a page cannot be read,
/// carrying the typed <see cref="OcctooError"/>. Like the events
/// enumerables, this is a deliberate exception to the results-first
/// contract: an <c>IAsyncEnumerable</c> has no failure track. Use the paged
/// <c>List</c> call to handle failures as results.
/// </summary>
public sealed class OcctooListException : Exception
{
    internal OcctooListException(OcctooError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>Why the list cannot continue.</summary>
    public OcctooError Error { get; }
}
