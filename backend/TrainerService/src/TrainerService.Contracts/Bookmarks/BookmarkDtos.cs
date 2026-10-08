namespace TrainerService.Contracts.Bookmarks;

/// <summary>Запрос на добавление закладки на вопрос.</summary>
public sealed record CreateBookmarkRequest(Guid TopicId, Guid QuestionId);

/// <summary>
///     Закладка вызывающего на вопрос, обогащённая контентом вопроса (#568 Ф2): текст вопроса
///     (<see cref="Stem"/>), сложность и заголовок темы — чтобы UI показал реальный вопрос и дал
///     войти в тест по закладке. <b>Без ключа грейдинга</b> (правильные ответы / эталон не
///     раскрываются — закладка это превью + вход в тест). Удалённый в ECS вопрос → <see cref="Stem"/>
///     пустой, тема может не резолвиться (<see cref="Difficulty"/>/<see cref="TopicTitle"/> = null) —
///     закладка всё равно возвращается (фронт может предложить её удалить).
/// </summary>
public sealed record BookmarkDto(
    Guid Id,
    Guid? TopicId,
    Guid QuestionId,
    DateTime CreatedAt,
    string? Stem,
    string? Difficulty,
    string? TopicTitle);