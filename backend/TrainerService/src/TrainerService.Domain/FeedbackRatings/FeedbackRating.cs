namespace TrainerService.Domain.FeedbackRatings;

/// <summary>
/// Оценка студентом AI-разбора («Разбор ИИ») открытого ответа: палец вверх / вниз (#691 t7).
/// Сериализуется в БД строкой (<c>HasConversion&lt;string&gt;()</c>), как и остальные enum'ы сервиса.
/// </summary>
public enum FeedbackRating
{
    UP,
    DOWN,
}
