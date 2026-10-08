using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CommentService.Contracts.Comments.Dtos;

namespace CommentService.Core.Features.Comments;

internal static class CommentAuthorEnricher
{
    public static Task EnrichWithAuthorInfoAsync(
        List<CommentDto> comments,
        IAuthServiceClient authServiceClient,
        CancellationToken cancellationToken)
        => EnrichManyWithAuthorInfoAsync([comments], authServiceClient, cancellationToken);

    // Один batch-вызов в AuthService и применение результата к каждому переданному списку.
    // Используется когда DTO живут в нескольких независимых коллекциях (root + preview-replies),
    // и копировать ссылки в одну плоскую List нельзя — `comments[i] = comments[i] with {...}`
    // мутирует только тот список, по которому идёт перебор, а не исходные коллекции.
    public static async Task EnrichManyWithAuthorInfoAsync(
        IReadOnlyList<List<CommentDto>> commentLists,
        IAuthServiceClient authServiceClient,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> authorIds = [];
        foreach (List<CommentDto> list in commentLists)
        {
            foreach (CommentDto comment in list)
            {
                authorIds.Add(comment.AuthorId);
            }
        }

        if (authorIds.Count == 0)
        {
            return;
        }

        var result = await authServiceClient.GetUsersByIdsAsync(
            [.. authorIds], cancellationToken);

        if (result.IsFailure)
        {
            return;
        }

        Dictionary<Guid, AuthUserLookupDto> userById = result.Value
            .ToDictionary(u => u.UserId);

        foreach (List<CommentDto> list in commentLists)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (userById.TryGetValue(list[i].AuthorId, out AuthUserLookupDto? user))
                {
                    list[i] = list[i] with
                    {
                        AuthorName = user.Name,
                        AuthorUsername = user.Username,
                        AuthorAvatarId = user.AvatarId,
                    };
                }
            }
        }
    }
}
