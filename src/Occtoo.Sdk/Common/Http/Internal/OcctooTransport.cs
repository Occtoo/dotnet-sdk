using CSharpFunctionalExtensions;

namespace Occtoo.Http.Internal;

/// <summary>
/// The one place SDK surfaces send requests through, so every feature client
/// upholds the same contract: expected transport failures become results, the
/// per-request timeout is enforced here (the underlying
/// <see cref="HttpClient"/> runs with an infinite timeout so long-lived streams
/// survive), and only the caller's own cancellation propagates as an exception.
/// </summary>
internal static class OcctooTransport
{
    internal static async Task<Result<HttpResponseMessage, OcctooError>> Send(
        HttpClient httpClient,
        HttpRequestMessage request,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var linked = timeout != Timeout.InfiniteTimeSpan
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        linked?.CancelAfter(timeout);
        var token = linked?.Token ?? cancellationToken;

        try
        {
            return await httpClient.SendAsync(request, completion, token).ConfigureAwait(false);
        }
        catch (OcctooCredentialException exception)
        {
            return exception.Error;
        }
        catch (HttpRequestException exception)
        {
            return new NetworkError($"Could not reach Occtoo: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TimeoutError("Occtoo did not answer in time.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Handlers the consumer layered into the pipeline (a resilience
            // handler's timeout or circuit breaker, say) may throw their own
            // types. The no-throw contract holds at this boundary.
            return new UnexpectedError($"The request failed unexpectedly: {exception.Message}");
        }
    }
}
