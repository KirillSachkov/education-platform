using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace SearchService.Core;

/// <summary>
/// Keyset-курсор для browse-пагинации Typesense. Хранит последний виденный
/// `updated_at_ticks`; следующая страница фильтруется как `updated_at_ticks:&lt;ticks`.
///
/// Пагинация работает через filter_by-границу — независимо от max_hits (default 10 000).
/// Для relevance-поиска (sort по _text_match) курсор не применяется: score не монотонный,
/// keyset-подход математически не работает.
///
/// Известное ограничение (#757): tiebreaker не реализован, потому что Typesense не
/// поддерживает range-операторы на строковом entity_id. До двухфазного добавления
/// числового UUID-tiebreaker документы с одинаковым updated_at_ticks на границе страницы
/// могут быть пропущены. Это не скрытое допущение, а отслеживаемый долг.
/// </summary>
public sealed record SearchCursor(long UpdatedAtTicks)
{
    public static string Encode(long updatedAtTicks)
    {
        var cursor = new SearchCursor(updatedAtTicks);
        string json = JsonSerializer.Serialize(cursor);
        return Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(json));
    }

    public static SearchCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            string json = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(cursor));
            return JsonSerializer.Deserialize<SearchCursor>(json);
        }
        catch
        {
            return null;
        }
    }
}
