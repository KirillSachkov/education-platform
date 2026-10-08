using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;

namespace TrainerService.Core.Features.Questions;

/// <summary>
///     Маппинг study-state вопроса в строковый статус для студенческих проекций. Отсутствие строки
///     <see cref="QuestionStudyState"/> = «новый» (<see cref="NEW"/>); иначе — имя <see cref="StudyStatus"/>
///     (SEEN/KNOWN/REVIEW/WRONG). NEW не хранится отдельным членом enum — это «нет строки».
/// </summary>
internal static class QuestionStatusText
{
    public const string NEW = "NEW";

    public static string From(QuestionStudyState? state) => state is null ? NEW : state.Status.ToString();
}
