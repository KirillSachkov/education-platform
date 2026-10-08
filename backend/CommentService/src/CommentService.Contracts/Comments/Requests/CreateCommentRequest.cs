using Common;

namespace CommentService.Contracts.Comments.Requests;

/// <summary>
/// Запрос на создание комментария / Request to create a comment
/// </summary>
/// <param name="EntityReference">Ссылка на сущность, к которой относится комментарий / Entity reference to which the comment belongs</param>
/// <param name="Content">Содержимое комментария / Content of the comment</param>
/// <param name="ParentId">Идентификатор родительского комментария (для ответов) / Parent comment identifier (for replies)</param>
public record CreateCommentRequest(
    EntityReferenceDto EntityReference,
    string Content,
    Guid? ParentId);
