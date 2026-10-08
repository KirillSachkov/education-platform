namespace TrainerService.Domain.SelfAssessment;

/// <summary>
/// Вердикт мягкой самооценки вопроса/теста (#691 t8). Пока единственное значение — <c>UNSURE</c>
/// («Не уверен» → вопрос паркуется в <see cref="TrainerService.Domain.StudyStatus.REVIEW"/>). Контракт намеренно минимален;
/// расширяемо новыми членами без миграции.
/// </summary>
public enum SelfAssessVerdict
{
    UNSURE,
}
