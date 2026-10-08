using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace TagService.Core;

/// <summary>
/// Стабильный курсор для алфавитной пагинации тегов: (Title, Id) как tiebreaker.
/// </summary>
public sealed record TagCursor(string Title, Guid LastId)
{
    public static string Encode(string title, Guid lastId)
    {
        var cursor = new TagCursor(title, lastId);
        string json = JsonSerializer.Serialize(cursor);
        return Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(json));
    }

    public static TagCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            string json = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(cursor));
            return JsonSerializer.Deserialize<TagCursor>(json);
        }
        catch
        {
            return null;
        }
    }
}
