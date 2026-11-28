using System.Globalization;
using System.Net.Http.Headers;
using Vogen;

namespace Occtoo;

/// <summary>
/// The version of a resource that guards concurrent changes. A change sends
/// the version it was based on; when someone else changed the resource
/// first, the change is a <see cref="ConflictError"/> — read it again and retry.
/// </summary>
[ValueObject<uint>(conversions: Conversions.None, toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ETag
{
    internal HttpRequestMessage AddTo(HttpRequestMessage request)
    {
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{Value.ToString(CultureInfo.InvariantCulture)}\""));
        return request;
    }
}
