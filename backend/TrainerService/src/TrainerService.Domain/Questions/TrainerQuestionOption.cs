namespace TrainerService.Domain.Questions;

/// <summary>
/// Child entity в <see cref="TrainerQuestion.Options"/>: вариант ответа на вопрос с
/// выбором (SINGLE/MULTI_CHOICE). PK выставляется EF через <c>TimeOrderedGuidValueGenerator</c>
/// (Id=Guid.Empty в factory — nav-collection child rule, docs/agents/backend-transactions.md
/// правило 4). Id варианта стабилен после сохранения и попадает в снапшот сессии + ключ грейдинга.
/// </summary>
public sealed class TrainerQuestionOption
{
    private TrainerQuestionOption() { } // EF

    private TrainerQuestionOption(Guid id, string text, bool isCorrect, int sortIndex)
    {
        Id = id;
        Text = text;
        IsCorrect = isCorrect;
        SortIndex = sortIndex;
    }

    public Guid Id { get; private set; }

    public string Text { get; private set; } = null!;

    public bool IsCorrect { get; private set; }

    public int SortIndex { get; private set; }

    internal static TrainerQuestionOption Create(string text, bool isCorrect, int sortIndex) =>
        new(Guid.Empty, text.Trim(), isCorrect, sortIndex);
}
