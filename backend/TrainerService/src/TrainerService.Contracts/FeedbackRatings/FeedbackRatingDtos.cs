namespace TrainerService.Contracts.FeedbackRatings;

// === POST /trainer/sessions/{sessionId}/answers/{itemId}/feedback-rating ===

/// <summary>
///     Тело запроса на оценку AI-разбора («Разбор ИИ») открытого ответа (#691 t7).
///     <paramref name="Rating"/> — <c>UP</c> (палец вверх) или <c>DOWN</c> (палец вниз).
/// </summary>
public sealed record RateAiFeedbackRequest(string Rating);

/// <summary>Текущая оценка AI-разбора item'а после апсерта (echo). Зеркало <c>AiFeedbackRatingDto</c>.</summary>
/// <param name="ItemId">Item сессии, чей AI-разбор оценили.</param>
/// <param name="Rating">Актуальная оценка — <c>UP</c> | <c>DOWN</c>.</param>
public sealed record AiFeedbackRatingDto(Guid ItemId, string Rating);
