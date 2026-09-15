using System.Globalization;
using System.Text;
using CSharpFunctionalExtensions;

namespace Occtoo.Http.Internal;

/// <summary>
/// Builds a relative request URI with escaped query parameters; absent values
/// add nothing.
/// </summary>
internal sealed class QueryString(string path)
{
    private readonly StringBuilder _builder = new(path);
    private char _separator = '?';

    internal QueryString Add(string name, string? value)
    {
        if (value is null)
            return this;

        _builder.Append(_separator).Append(name).Append('=').Append(Uri.EscapeDataString(value));
        _separator = '&';
        return this;
    }

    internal QueryString Add(string name, Maybe<string> value) => Add(name, value.GetValueOrDefault());

    internal QueryString Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

    internal QueryString Add(string name, Maybe<Guid> value) =>
        value.HasValue ? Add(name, value.Value.ToString("D")) : this;

    // Timestamps travel as UTC with a Z suffix; a "+" offset would need escaping.
    internal QueryString Add(string name, Maybe<DateTimeOffset> value) =>
        value.HasValue ? Add(name, value.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)) : this;

    internal QueryString Add(string name, Maybe<PageCursor> value) =>
        value.HasValue ? Add(name, value.Value.Value) : this;

    internal QueryString AddEnum<TEnum>(string name, Maybe<TEnum> value)
        where TEnum : struct, Enum =>
        value.HasValue ? Add(name, value.Value.ToString()) : this;

    internal QueryString AddEach(string name, IReadOnlyList<string> values)
    {
        foreach (var value in values)
            Add(name, value);

        return this;
    }

    internal Uri ToUri() => new(_builder.ToString(), UriKind.Relative);
}
