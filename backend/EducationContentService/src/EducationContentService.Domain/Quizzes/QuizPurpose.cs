namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Назначение квиза. Хранится строкой (<c>VARCHAR(50)</c>, без CHECK-constraint —
///     как <c>Course.Kind</c>: новое значение = +1 член enum, миграция не нужна).
///     Immutable после создания.
/// </summary>
public enum QuizPurpose
{
    /// <summary>Проверка усвоения материала (квиз привязан к материалу или standalone).</summary>
    MATERIAL_CHECK,

    /// <summary>Входной тест уровня (level-test воронка). Всегда standalone — без материала.</summary>
    LEVEL_TEST,

    /// <summary>
    ///     Банк вопросов тренажёра (раздел <c>/trainer</c>, #568). Всегда standalone — привязка
    ///     к теме живёт в TrainerService (<c>trainer.topic_banks.quiz_id</c>), не в ECS.
    /// </summary>
    TRAINER
}
