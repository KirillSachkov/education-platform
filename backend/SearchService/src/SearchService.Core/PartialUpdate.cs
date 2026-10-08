using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;

namespace SearchService.Core;

public sealed class PartialUpdate<TDocument>
    where TDocument : class
{
    private readonly Dictionary<string, object?> _fields = new(StringComparer.Ordinal);

    public PartialUpdate<TDocument> Set<TValue>(
        Expression<Func<TDocument, TValue>> selector,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(selector);

        string fieldName = GetFieldName(selector);
        _fields[fieldName] = NormalizeValue(value);

        return this;
    }

    public Dictionary<string, object?> Build() => new(_fields, StringComparer.Ordinal);

    private static object? NormalizeValue(object? value) =>
        value is Enum enumValue
            ? enumValue.ToString()
            : value;

    private static string GetFieldName<TValue>(Expression<Func<TDocument, TValue>> selector)
    {
        if (selector.Body is not MemberExpression memberExpression)
        {
            // boundary: API contract guard — caller passed a non-property selector.
            throw new ArgumentException("Selector must be a property expression.", nameof(selector));
        }

        if (memberExpression.Member is not PropertyInfo propertyInfo)
        {
            // boundary: API contract guard — caller passed a non-property member.
            throw new ArgumentException("Selector must target a property.", nameof(selector));
        }

        JsonPropertyNameAttribute? jsonName =
            propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();

        return jsonName?.Name ?? propertyInfo.Name;
    }
}
