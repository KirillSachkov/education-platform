namespace TrainerService.Domain.MockInterviews;

/// <summary>
/// Child entity в <see cref="MockInterview.Questions"/>: одна курированная ссылка автора на
/// конкретный вопрос локального банка тренажёра (<c>QuestionId</c> → <c>TrainerQuestion.Id</c>).
/// Автор руками отбирает вопросы в пул мок-собеса; студенческая сессия тянет из него случайную
/// подвыборку размера <see cref="MockInterview.QuestionsPerSession"/>. <c>SortIndex</c> = позиция
/// в авторском списке (стабильный порядок для редактора). PK выставляется EF через
/// <c>TimeOrderedGuidValueGenerator</c> (Id=Guid.Empty в factory — nav-collection child rule,
/// см. docs/agents/backend-transactions.md правило 4). #585.
/// </summary>
public sealed class MockInterviewQuestion
{
    private MockInterviewQuestion() { } // EF

    private MockInterviewQuestion(Guid id, Guid questionId, int sortIndex)
    {
        Id = id;
        QuestionId = questionId;
        SortIndex = sortIndex;
    }

    public Guid Id { get; private set; }

    /// <summary>Вопрос локального банка тренажёра (<c>TrainerQuestion.Id</c>).</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>Позиция в авторском курированном списке.</summary>
    public int SortIndex { get; private set; }

    internal static MockInterviewQuestion Create(Guid questionId, int sortIndex) =>
        new(Guid.Empty, questionId, sortIndex);
}
