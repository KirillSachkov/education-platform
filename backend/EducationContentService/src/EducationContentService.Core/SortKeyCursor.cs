using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace EducationContentService.Core;

/// <summary>
///     Cursor for SortKey-based pagination (course catalog / by-author lists).
///     SortKey is fractional-indexing string, lexicographically comparable. Id breaks ties.
/// </summary>
public sealed record SortKeyCursor(string SortKey, Guid LastId)
{
    public static string Encode(string sortKey, Guid lastId)
    {
        var cursor = new SortKeyCursor(sortKey, lastId);
        string json = JsonSerializer.Serialize(cursor);
        return Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(json));
    }

    public static SortKeyCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            string json = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(cursor));
            return JsonSerializer.Deserialize<SortKeyCursor>(json);
        }
        catch
        {
            return null;
        }
    }
}
