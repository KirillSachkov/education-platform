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

    /// <summary>Historical question-bank purpose. Retained to read persisted quizzes without renumbering the enum.</summary>
    TRAINER
}