using Common;
using ProgressService.Domain.Bookmarks;

namespace ProgressService.Domain;

/// <summary>
///     Фабрика доменных ошибок сервиса прогресса.
/// </summary>
public static class ProgressErrors
{
    public static Error CounterCannotBeNegative(string counterName) =>
        Error.Validation("progress.counter.negative", $"{counterName} не может быть отрицательным");

    public static Error CounterCannotExceedTotal(string completedName, string totalName) =>
        Error.Validation(
            "progress.counter.exceeds.total",
            $"{completedName} не может превышать {totalName}");

    public static Error EnrollmentAlreadyExists() =>
        Error.Conflict("course.enrollment.already.exists", "Запись на курс уже существует");

    public static Error EnrollmentNotFound() =>
        Error.NotFound("course.enrollment.not.found", "Запись на курс не найдена");

    public static Error MaterialViewNotFound() =>
        Error.NotFound("material.view.not.found", "Просмотр материала не найден");

    public static Error MaterialAccessDenied() =>
        Error.Authorization(
            "material.access.denied",
            "У вас нет доступа к этому материалу. Запишитесь на курс или выберите полный тариф");

    public static Error IssueAuthorQuestionMessageRequired() =>
        Error.Validation(
            "issue.author_question.message.required",
            "Опишите свой вопрос автору");

    public static Error IssueAuthorQuestionMessageTooLong(int maxLength) =>
        Error.Validation(
            "issue.author_question.message.too.long",
            $"Текст вопроса не может превышать {maxLength} символов");

    public static Error IssueAuthorQuestionAccessDenied() =>
        Error.Authorization(
            "issue.author_question.access.denied",
            "Это задание вам недоступно — задать вопрос автору нельзя");

    public static Error ModuleProgressNotFound() =>
        Error.NotFound("module.progress.not.found", "Прогресс по модулю не найден");

    public static Error ModuleProgressAlreadyExists() =>
        Error.Conflict("module.progress.already.exists", "Прогресс по модулю уже существует");

    public static Error ModuleProgressNotStarted() =>
        Error.Conflict("module.progress.not.started", "Прогресс по модулю ещё не начат");

    public static Error ModuleItemProgressNotFound() =>
        Error.NotFound("module.item.progress.not.found", "Прогресс по элементу модуля не найден");

    public static Error ModuleItemProgressAlreadyExists() =>
        Error.Conflict("module.item.progress.already.exists", "Прогресс по элементу модуля уже существует");

    public static Error ModuleItemProgressNotStarted() =>
        Error.Conflict("module.item.progress.not.started", "Прогресс по элементу модуля ещё не начат");

    public static Error IssueProgressNotFound() =>
        Error.NotFound("issue.progress.not.found", "Прогресс по задаче не найден");

    public static Error IssueSubmissionNotFound() =>
        Error.NotFound("issue.submission.not.found", "Отправка задачи не найдена");

    public static Error ProjectProgressNotFound() =>
        Error.NotFound("project.progress.not.found", "Прогресс по проекту не найден");

    public static Error ProjectProgressAlreadyExists() =>
        Error.Conflict("project.progress.already.exists", "Прогресс по проекту уже существует");

    public static Error ProjectProgressNotStarted() =>
        Error.Conflict("project.progress.not.started", "Прогресс по проекту ещё не начат");

    public static Error ModuleCannotBeCompleted(string completedName, string totalName) =>
        Error.Validation(
            "module.progress.cannot.complete",
            $"{completedName} должен быть равен {totalName}");

    public static Error ProjectCannotBeCompleted(string completedName, string totalName) =>
        Error.Validation(
            "project.progress.cannot.complete",
            $"{completedName} должен быть равен {totalName}");

    public static Error InvalidStatusTransition(string entityName, string currentStatus, string action) =>
        Error.Conflict(
            $"{entityName}.invalid.transition",
            $"Невозможно выполнить «{action}» из статуса «{currentStatus}»");

    public static Error AttemptNumberMustBePositive(string fieldName) =>
        Error.Validation("issue.submission.attempt.invalid", $"{fieldName} должен быть больше нуля");

    public static Error SubmissionUrlMustBeAbsoluteHttpUrl(string fieldName) =>
        Error.Validation(
            "issue.submission.url.invalid",
            $"{fieldName} должен быть абсолютным http/https URL");

    public static Error SubmissionUrlDomainNotAllowed(string fieldName) =>
        Error.Validation(
            "issue.submission.url.domain.not.allowed",
            $"{fieldName} должен ссылаться на разрешённый домен (github.com, sachkov-learn.net)");

    public static Error SubmissionUrlNotPullRequest() =>
        Error.Validation(
            "issue.submission.not_pull_request",
            "Ссылка должна вести на конкретный pull request, например https://github.com/owner/repo/pull/123");

    public static Error ValueLengthExceeded(string fieldName, int maxLength) =>
        Error.Validation($"{fieldName}.length.exceeded", $"{fieldName} не должен превышать {maxLength} символов");

    public static Error ContentGrantAlreadyRevoked() =>
        Error.Conflict("content.grant.already.revoked", "Доступ к контенту уже отозван");

    public static Error ContentGrantAlreadyExists() =>
        Error.Conflict("content.grant.already.exists", "Доступ к контенту уже существует");

    public static Error PaidCourseManualEnrollmentOnly() =>
        Error.Validation("course.enrollment.paid.manual.only",
            "Самозапись недоступна. Выберите подходящий тариф");

    public static Error TrialNotAvailable() =>
        Error.Validation(
            "course.enrollment.trial.not.available",
            "На этом курсе нет пробных материалов — запись доступна только через администратора");

    public static Error IssueLockedForTrial() =>
        Error.Authorization(
            "issue.locked.for.trial",
            "Это задание доступно только при полной записи на курс");

    public static Error IssueSubmissionNotAllowedByPlan() =>
        Error.Authorization(
            "issue.submission.not.allowed.by.plan",
            "Ваш план не включает отправку решений. Обновите план, чтобы получить полный доступ.");

    public static Error CourseNotPublished() =>
        Error.Validation("course.not.published", "Курс не опубликован");

    public static Error CourseManagementForbidden(Guid courseId) =>
        Error.Authorization("course.management.forbidden", "Нет прав на управление студентами курса");

    public static Error BookmarkTargetTypeNotSupported(EntityType entityType) =>
        Error.Validation(
            "bookmark.target.type.not.supported",
            $"Тип сущности «{entityType}» не поддерживается для закладок");

    public static Error BookmarkTargetNotFound(BookmarkEntityReference target) =>
        Error.NotFound(
            "bookmark.target.not.found",
            $"Цель закладки «{target.Type}» с ID «{target.Id}» не найдена");

    public static Error NoUsersProvided() =>
        Error.Validation("course.enrollment.no.users", "Не указаны пользователи для записи");

    public static Error EducationContentServiceUnavailable() =>
        Error.Failure(
            "education.content.service.unavailable",
            "Сервис образовательного контента недоступен");

    public static Error QuizAccessDenied() =>
        Error.Authorization(
            "quiz.access.denied",
            "У вас нет доступа к этому квизу. Запишитесь на курс или выберите полный тариф");

    public static Error QuizNotInCourse() =>
        Error.NotFound("quiz.not.in.course", "Тест не входит в этот курс");

    public static Error QuizQuestionNotFound() =>
        Error.NotFound("quiz.question.not.found", "Вопрос не найден в этом квизе");

    public static Error QuizAttemptTooManySelectedOptions(int maxOptions) =>
        Error.Validation(
            "quiz.attempt.too.many.selected.options",
            $"Нельзя выбрать больше {maxOptions} вариантов ответа на один вопрос");

    public static Error QuizAttemptTextAnswerTooLong(int maxLength) =>
        Error.Validation(
            "quiz.attempt.text.answer.too.long",
            $"Текстовый ответ не может быть длиннее {maxLength} символов");

    public static Error QuizAttemptDuplicateQuestionIds() =>
        Error.Validation(
            "quiz.attempt.duplicate.question.ids",
            "Ответы содержат повторяющиеся идентификаторы вопросов");

    public static Error QuizAttemptTooManyAnswers(int maxAnswers) =>
        Error.Validation(
            "quiz.attempt.too.many.answers",
            $"Попытка не может содержать больше {maxAnswers} ответов");

    public static Error QuizChangedReload() =>
        Error.Conflict(
            "quiz.changed.reload",
            "Тест был обновлён автором. Обновите страницу и пройдите заново.");

}