namespace TrainerService.Contracts.SelfAssessment;

// === PATCH /trainer/sessions/{sessionId}/items/{itemId}/self-assess ===

/// <summary>
///     Тело запроса на мягкую самооценку item'а сессии (#691 t8). <paramref name="Verdict"/> — пока
///     только <c>UNSURE</c> («Не уверен» → вопрос уходит в REVIEW / «На повтор»). Неизвестное → 400.
/// </summary>
public sealed record SelfAssessRequest(string Verdict);

/// <summary>Новый study-status вопроса после самооценки (echo). <paramref name="Status"/> — <c>REVIEW</c>.</summary>
/// <param name="ItemId">Item сессии, чей вопрос переоценили.</param>
/// <param name="Status">Актуальный study-status вопроса (<c>REVIEW</c>).</param>
public sealed record SelfAssessmentDto(Guid ItemId, string Status);
