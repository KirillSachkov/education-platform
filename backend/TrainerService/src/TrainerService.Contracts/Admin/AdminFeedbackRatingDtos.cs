namespace TrainerService.Contracts.Admin;

// === GET /trainer/admin/stats/feedback-ratings?days=N ===

/// <summary>
///     Admin-агрегат оценок AI-разбора (#691 t7): по-вопросная разбивка 👍/👎 за окно <c>days</c>
///     (<c>ai_feedback_ratings.created_at &gt;= now - days</c>, clamp 1..365, default 30), джойнятся к
///     собственным вопросам тренажёра (<c>trainer_questions → topic_banks → topics</c>). Упорядочено
///     «худшие сверху» (наибольший down-rate), чтобы владелец видел вопросы, где AI-разбор не нравится
///     студентам, — кандидаты на правку промпта/эталона. Read-only.
/// </summary>
/// <param name="Days">Эффективное окно в днях (clamp 1..365, default 30).</param>
/// <param name="Questions">Вопросы, у которых в окне есть хотя бы одна оценка AI-разбора.</param>
public sealed record AdminFeedbackRatingStatsDto(
    int Days,
    IReadOnlyList<AdminFeedbackRatingItemDto> Questions);

/// <summary>Оценки AI-разбора одного вопроса.</summary>
/// <param name="QuestionId">Идентичность вопроса собственного банка тренажёра.</param>
/// <param name="Stem">Текст вопроса (для показа в таблице).</param>
/// <param name="QuestionType">Тип вопроса (как правило OPEN_TEXT — только у них есть AI-разбор).</param>
/// <param name="Difficulty">JUNIOR / MIDDLE / SENIOR или null.</param>
/// <param name="TopicId">Тема-владелец (через банк).</param>
/// <param name="TopicTitle">Заголовок темы (для группировки в UI).</param>
/// <param name="BankId">Банк-владелец вопроса.</param>
/// <param name="Up">Сколько 👍.</param>
/// <param name="Down">Сколько 👎.</param>
/// <param name="Total">Всего оценок (<see cref="Up"/> + <see cref="Down"/>).</param>
/// <param name="DownRate">Доля 👎 = <see cref="Down"/> / <see cref="Total"/> (0..1).</param>
public sealed record AdminFeedbackRatingItemDto(
    Guid QuestionId,
    string Stem,
    string QuestionType,
    string? Difficulty,
    Guid TopicId,
    string TopicTitle,
    Guid BankId,
    long Up,
    long Down,
    long Total,
    double DownRate);
