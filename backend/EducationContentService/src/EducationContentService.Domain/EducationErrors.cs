namespace EducationContentService.Domain;

/// <summary>
///     Фабрика доменных ошибок сервиса образовательного контента.
/// </summary>
public static class EducationErrors
{
    public static Error NotFound(string entityName, Guid id) =>
        Error.NotFound("entity.not.found", $"{EntityLabel(entityName)} с ID {id} не найден");

    public static Error TitleAlreadyExists(string entityName, string title) =>
        Error.Conflict("title.already.exists", $"{EntityLabel(entityName)} с названием \"{title}\" уже существует");

    public static Error CannotPublishEmptyCourse() =>
        Error.Conflict("course.publish.empty", "Нельзя опубликовать пустой курс");

    public static Error CannotPublishWithDraftChildren() =>
        Error.Conflict(
            "course.publish.draft.children",
            "Нельзя опубликовать курс: все вложенные элементы должны быть в статусе Published");

    public static Error ItemNotFound(string containerName, Guid itemId) =>
        Error.NotFound("item.not.found", $"Элемент {EntityLabelGenitive(containerName)} с ID {itemId} не найден");

    public static Error IssueRequiresProject() =>
        Error.Validation("issue.requires.project", "Задача не может существовать без проекта");

    public static Error IssueAlreadyInModule(Guid issueId) =>
        Error.Conflict(
            "issue.already.in.module",
            $"Задача {issueId} уже привязана к другому модулю — одна задача может принадлежать только одному модулю");

    public static Error IssuesNotAllowedInIntensive() =>
        Error.Validation(
            "course.intensive.no.issues",
            "Нельзя привязать задание к интенсиву — интенсивы содержат только материалы");

    public static Error IssuesNotAllowedInMarathon() =>
        Error.Validation(
            "course.marathon.no.issues",
            "Марафон не может содержать задания");

    public static Error InvalidCourseKind() =>
        Error.Validation("course.kind.invalid", "Некорректный тип курса");

    public static Error CourseKindChangeBlockedByIssues() =>
        Error.Validation(
            "course.kind.has.issues",
            "Нельзя сделать курс интенсивом или марафоном — в его модулях есть задания. Сначала уберите задания");

    public static Error CourseAuthorUnchanged() =>
        Error.Validation("course.author.unchanged", "Курс уже принадлежит этому автору");

    public static Error MaterialAlreadyInCourse(Guid materialId, Guid courseId) =>
        Error.Conflict(
            "material.already.in.course",
            $"Материал {materialId} уже прикреплён к курсу {courseId}");

    public static Error DatabaseError() =>
        Error.Failure("education.database.error", "Ошибка базы данных при работе с сервисом - education");

    public static Error OperationCancelled() =>
        Error.Failure("education.operation.cancelled", "Операция была отменена");

    /// <summary>
    ///     Generic переход статуса для Course/Module/Roadmap.
    ///     Код ошибки оставлен legacy `lesson.invalid.status.transition` для обратной совместимости с фронтом —
    ///     не менять, иначе сломаем i18n-override и логику ловли ошибок на клиенте.
    /// </summary>
    public static Error InvalidStatusTransition(string from, string to) =>
        Error.Conflict("lesson.invalid.status.transition", $"Нельзя перевести в статус {to} из {from}");

    public static Error InvalidProjectStatusTransition(string from, string to) =>
        Error.Conflict("project.invalid.status.transition", $"Нельзя перевести проект из статуса {from} в {to}");

    public static Error InvalidAccessType() =>
        Error.Validation("content.access_type.invalid", "Некорректный тип доступа");

    public static Error UnauthorizedAccess() =>
        Error.Authentication(
            "content.access.unauthorized",
            "Контент недоступен без авторизации или записи на курс");

    public static Error AccessDenied() =>
        Error.Authorization("content.access.denied", "Недостаточно прав для доступа к контенту");

    public static Error AuthorshipRequired() =>
        Error.Authorization("content.authorship.required", "Операция доступна только автору курса");

    public static Error RoadmapAlreadyExistsForCourse(Guid courseId) =>
        Error.Conflict("roadmap.course.duplicate", $"Роадмап для курса {courseId} уже существует");

    public static Error SlugAlreadyExists(string slug) =>
        Error.Conflict("roadmap.slug.duplicate", $"Роадмап со слагом \"{slug}\" уже существует");

    public static Error CourseSlugAlreadyExists(string slug) =>
        Error.Conflict("course.slug.duplicate", $"Курс со слагом \"{slug}\" уже существует");

    public static Error InvalidMaterialStatusTransition(string from, string to) =>
        Error.Conflict("material.invalid.status.transition", $"Нельзя перевести материал из статуса {from} в {to}");

    public static Error CannotPublishMaterialWithoutContent() =>
        Error.Validation(
            "material.publish.content_or_video_required",
            "Материал нельзя опубликовать без текста или видео");

    public static Error MaterialAccessRequiresCourse() =>
        Error.Validation(
            "material.access.requires_course",
            "Уровень доступа FREE/ENROLLED доступен только для материалов, привязанных хотя бы к одному курсу");

    public static Error ModuleNotAttachedToCourse(Guid moduleId) =>
        Error.Validation(
            "module.not.attached.to.course",
            $"Модуль {moduleId} не привязан ни к одному курсу");

    public static Error ModuleCourseMismatch(Guid moduleId, Guid courseId) =>
        Error.Validation(
            "module.course.mismatch",
            $"Модуль {moduleId} не принадлежит курсу {courseId}");

    public static Error MaterialNotFound(Guid materialId) =>
        Error.NotFound("material.not.found", $"Материал {materialId} не найден");

    public static Error InvalidCollectionStatusTransition(string from, string to) =>
        Error.Conflict("collection.invalid.status.transition", $"Нельзя перевести подборку из статуса {from} в {to}");

    public static Error CannotPublishEmptyCollection() =>
        Error.Validation("collection.publish.empty", "Нельзя опубликовать подборку без материалов");

    public static Error CollectionNotFound(Guid collectionId) =>
        Error.NotFound("collection.not.found", $"Подборка {collectionId} не найдена");

    // Код стабилен с времён material-only подборок — после #491 (generic items)
    // ошибка покрывает и материалы, и квизы.
    public static Error CollectionItemDuplicate(Guid referenceId) =>
        Error.Conflict("collection.item.duplicate", $"Элемент {referenceId} уже добавлен в эту секцию");

    public static Error InvalidCollectionItemType(string itemType) =>
        Error.Validation("collection.item.type.invalid", $"Некорректный тип элемента подборки: {itemType}");

    public static Error CannotPinSpaceLevelCollection() =>
        Error.Validation("collection.pin.requires.course", "Закрепить можно только подборку, привязанную к курсу");

    public static Error CollectionAccessRequiresCourse() =>
        Error.Validation(
            "collection.access.requires.course",
            "Уровень доступа FREE/ENROLLED доступен только для подборок, привязанных к курсу");

    public static Error QuizNotFound(Guid quizId) =>
        Error.NotFound("quiz.not.found", $"Квиз {quizId} не найден");

    public static Error MaterialQuizNotFound(Guid materialId) =>
        Error.NotFound("quiz.not.found", $"У материала {materialId} нет опубликованного квиза");

    public static Error CannotPublishEmptyQuiz() =>
        Error.Validation("quiz.publish.empty", "Нельзя опубликовать квиз без вопросов");

    public static Error InvalidQuizStatusTransition(string from, string to) =>
        Error.Conflict("quiz.invalid.status.transition", $"Нельзя перевести квиз из статуса {from} в {to}");

    public static Error InvalidQuizPassingScore() =>
        Error.Validation("quiz.passing_score.invalid", "Проходной балл должен быть от 0 до 100");

    public static Error QuizQuestionsLimitExceeded(int max) =>
        Error.Validation("quiz.questions.limit.exceeded", $"Квиз не может содержать больше {max} вопросов");

    public static Error InvalidQuizQuestionType(string type) =>
        Error.Validation("quiz.question.type.invalid", $"Некорректный тип вопроса: {type}");

    public static Error QuizQuestionInvalid(string reason) =>
        Error.Validation("quiz.question.invalid", $"Некорректный вопрос квиза: {reason}");

    public static Error QuizOptionInvalid(string reason) =>
        Error.Validation("quiz.question.option.invalid", $"Некорректный вариант ответа: {reason}");

    public static Error QuizOptionsCountInvalid(int min, int max) =>
        Error.Validation(
            "quiz.question.options.count.invalid",
            $"Вопрос с выбором должен содержать от {min} до {max} вариантов ответа");

    public static Error QuizCorrectOptionsInvalid(string reason) =>
        Error.Validation("quiz.question.correct_options.invalid", $"Некорректные правильные варианты: {reason}");

    public static Error InvalidQuizPurpose(string purpose) =>
        Error.Validation("quiz.purpose.invalid", $"Некорректное назначение квиза: {purpose}");

    public static Error InvalidQuizQuestionDifficulty(string difficulty) =>
        Error.Validation("quiz.question.difficulty.invalid", $"Некорректная сложность вопроса: {difficulty}");

    private static string EntityLabel(string entityName) => entityName switch
    {
        "Course" => "Курс",
        "Module" => "Модуль",
        "Lesson" => "Урок",
        "Project" => "Проект",
        "Issue" => "Задача",
        "Roadmap" => "Роадмап",
        "Article" => "Статья",
        "Material" => "Материал",
        "Quiz" => "Квиз",
        "Collection" => "Подборка",
        "CollectionSection" => "Секция подборки",
        "CollectionItem" => "Элемент подборки",
        _ => entityName,
    };

    private static string EntityLabelGenitive(string entityName) => entityName switch
    {
        "Course" => "курса",
        "Module" => "модуля",
        "Lesson" => "урока",
        "Project" => "проекта",
        "Issue" => "задачи",
        "Roadmap" => "роадмапа",
        "Article" => "статьи",
        "Material" => "материала",
        "Quiz" => "квиза",
        "Collection" => "подборки",
        "CollectionSection" => "секции подборки",
        "CollectionItem" => "элемента подборки",
        _ => entityName,
    };
}