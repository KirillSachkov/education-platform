namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Назначение квиза. Хранится строкой (<c>VARCHAR(50)</c>, без CHECK-constraint —
///     как <c>Course.Kind</c>: новое значение = +1 член enum, миграция не нужна).
///     Immutable после создания.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute",
    Justification = "Purpose is a single choice; value 1 is retired and historical TRAINER keeps value 2.")]
public enum QuizPurpose
{
    /// <summary>Проверка усвоения материала (квиз привязан к материалу или standalone).</summary>
    MATERIAL_CHECK = 0,

    /// <summary>Historical question-bank purpose. Retained to read persisted quizzes without renumbering the enum.</summary>
    TRAINER = 2
}