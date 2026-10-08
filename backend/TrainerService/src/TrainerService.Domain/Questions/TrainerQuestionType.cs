namespace TrainerService.Domain.Questions;

/// <summary>
/// Тип вопроса собственного банка тренажёра (#623). Значения зеркалят строковые
/// константы <c>AnswerGrader</c> (SINGLE_CHOICE / MULTI_CHOICE / EXACT_TEXT / OPEN_TEXT),
/// чтобы грейдер работал по <c>Type.ToString()</c> без дополнительного маппинга.
/// </summary>
public enum TrainerQuestionType
{
    SINGLE_CHOICE,
    MULTI_CHOICE,
    EXACT_TEXT,
    OPEN_TEXT,
}
