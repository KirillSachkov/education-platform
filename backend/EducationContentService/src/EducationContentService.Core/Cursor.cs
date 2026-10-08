using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace EducationContentService.Core;

public sealed record Cursor(DateTimeOffset CreatedAt, Guid LastId)
{
    public static string Encode(DateTimeOffset createdAt, Guid lastId)
    {
        var cursor = new Cursor(createdAt, lastId);
        string json = JsonSerializer.Serialize(cursor);
        return Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(json));
    }

    public static Cursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            string json = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(cursor));
            return JsonSerializer.Deserialize<Cursor>(json);
        }
        catch
        {
            return null;
        }
    }
}
