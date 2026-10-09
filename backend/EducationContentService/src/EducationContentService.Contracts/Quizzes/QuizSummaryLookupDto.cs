namespace EducationContentService.Contracts.Quizzes;

/// <summary>Краткая S2S-проекция квиза для обогащения списков попыток и аналитики.
///     Отсутствующие ID не попадают в ответ; ответы вопросов не раскрываются.</summary>
public sealed record QuizSummaryLookupDto(Guid Id, string Title, string Purpose, Guid? CourseId);

public sealed record GetQuizSummariesRequest(IReadOnlyCollection<Guid> Ids);