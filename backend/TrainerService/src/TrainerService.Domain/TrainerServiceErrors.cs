namespace TrainerService.Domain;

/// <summary>
/// Domain error factories. Codes follow "trainer.{entity}.{condition}" convention.
/// Messages are Russian by convention (user-facing via API Envelope.message).
/// </summary>
public static class TrainerServiceErrors
{
    public static class Track
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("trainer.track.not.found", $"Трек {id} не найден.");

        public static Error SlugRequired() =>
            Error.Validation("trainer.track.slug.required", "Slug трека обязателен.");

        public static Error SlugTooLong(int max) =>
            Error.Validation("trainer.track.slug.too.long", $"Slug не может быть длиннее {max} символов.");

        public static Error SlugInvalid() =>
            Error.Validation("trainer.track.slug.invalid", "Slug может содержать только строчные латинские буквы, цифры и дефис.");

        public static Error TitleRequired() =>
            Error.Validation("trainer.track.title.required", "Название трека обязательно.");

        public static Error TitleTooLong(int max) =>
            Error.Validation("trainer.track.title.too.long", $"Название не может быть длиннее {max} символов.");

        public static Error DescriptionTooLong(int max) =>
            Error.Validation("trainer.track.description.too.long", $"Описание не может быть длиннее {max} символов.");

        public static Error InvalidStack(string raw) =>
            Error.Validation("trainer.track.invalid.stack", $"Недопустимый стек: «{raw}». Ожидается CSHARP, TYPESCRIPT или DEVOPS.");

        public static Error SlugAlreadyExists(string slug) =>
            Error.Conflict("trainer.track.slug.already.exists", $"Трек со slug «{slug}» уже существует.");
    }

    public static class MockInterview
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("trainer.mock.interview.not.found", $"Симуляция собеседования {id} не найдена.");

        public static Error SlugRequired() =>
            Error.Validation("trainer.mock.interview.slug.required", "Slug симуляции обязателен.");

        public static Error SlugTooLong(int max) =>
            Error.Validation("trainer.mock.interview.slug.too.long", $"Slug не может быть длиннее {max} символов.");

        public static Error SlugInvalid() =>
            Error.Validation("trainer.mock.interview.slug.invalid", "Slug может содержать только строчные латинские буквы, цифры и дефис.");

        public static Error TitleRequired() =>
            Error.Validation("trainer.mock.interview.title.required", "Название симуляции обязательно.");

        public static Error TitleTooLong(int max) =>
            Error.Validation("trainer.mock.interview.title.too.long", $"Название не может быть длиннее {max} символов.");

        public static Error DescriptionTooLong(int max) =>
            Error.Validation("trainer.mock.interview.description.too.long", $"Описание не может быть длиннее {max} символов.");

        public static Error SlugAlreadyExists(string slug) =>
            Error.Conflict("trainer.mock.interview.slug.already.exists", $"Симуляция со slug «{slug}» уже существует.");

        public static Error InvalidPerSession(int max) =>
            Error.Validation("trainer.mock.invalid_per_session", $"Число вопросов на сессию должно быть пустым или в диапазоне 1..{max}.");

        public static Error NoQuestionSource() =>
            Error.Validation("trainer.mock.interview.no.question.source", "Нельзя опубликовать симуляцию без вопросов: добавьте хотя бы один вопрос или тему.");
    }

    public static class Topic
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("trainer.topic.not.found", $"Тема {id} не найдена.");

        public static Error TrackRequired() =>
            Error.Validation("trainer.topic.track.required", "Тема должна относиться к треку.");

        public static Error InvalidDirection(string raw) =>
            Error.Validation("trainer.topic.invalid.direction", $"Недопустимое направление: «{raw}». Ожидается BACKEND, FRONTEND, FULLSTACK или GENERAL.");

        public static Error SlugRequired() =>
            Error.Validation("trainer.topic.slug.required", "Slug темы обязателен.");

        public static Error SlugTooLong(int max) =>
            Error.Validation("trainer.topic.slug.too.long", $"Slug не может быть длиннее {max} символов.");

        public static Error SlugInvalid() =>
            Error.Validation("trainer.topic.slug.invalid", "Slug может содержать только строчные латинские буквы, цифры и дефис.");

        public static Error TitleRequired() =>
            Error.Validation("trainer.topic.title.required", "Название темы обязательно.");

        public static Error TitleTooLong(int max) =>
            Error.Validation("trainer.topic.title.too.long", $"Название не может быть длиннее {max} символов.");

        public static Error AreaRequired() =>
            Error.Validation("trainer.topic.area.required", "Область темы обязательна.");

        public static Error AreaTooLong(int max) =>
            Error.Validation("trainer.topic.area.too.long", $"Область не может быть длиннее {max} символов.");

        public static Error DescriptionTooLong(int max) =>
            Error.Validation("trainer.topic.description.too.long", $"Описание не может быть длиннее {max} символов.");

        public static Error NotPublished(Guid id) =>
            Error.Validation("trainer.topic.not.published", $"Тема {id} не опубликована.");

        public static Error Locked() =>
            Error.Authorization("trainer.topic.locked", "Эта тема доступна только по платному плану.");

        public static Error NoBankAvailable() =>
            Error.Validation("trainer.topic.no.bank.available", "У темы нет доступного банка вопросов.");

        public static Error SlugAlreadyExists(string slug) =>
            Error.Conflict("trainer.topic.slug.already.exists", $"Тема со slug «{slug}» уже существует.");

        public static Error HasBanks(Guid id, int bankCount) =>
            Error.Conflict("trainer.topic.has.banks", $"Нельзя удалить тему {id}: к ней привязано банков вопросов — {bankCount}. Сначала отвяжите банки.");
    }

    public static class Bank
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("trainer.bank.not.found", $"Банк вопросов {id} не найден.");

        public static Error InvalidTier(string raw) =>
            Error.Validation("trainer.bank.invalid.tier", $"Недопустимый tier банка: «{raw}». Ожидается FREE или PAID.");

        public static Error InvalidDifficulty(string raw) =>
            Error.Validation("trainer.bank.invalid.difficulty", $"Недопустимая сложность: «{raw}». Ожидается JUNIOR, MIDDLE или SENIOR.");

        public static Error InvalidPurpose(string raw) =>
            Error.Validation("trainer.bank.invalid.purpose", $"Недопустимое назначение банка: «{raw}». Ожидается STUDY или MOCK.");

        public static Error AlreadyExists(Guid quizId) =>
            Error.Conflict("trainer.bank.already.exists", $"Банк для квиза {quizId} уже привязан к теме.");

        public static Error EmptyQuiz(Guid quizId) =>
            Error.Validation("trainer.bank.empty.quiz", $"Квиз {quizId} не содержит вопросов.");
    }

    public static class Question
    {
        public static Error NotFound(Guid questionId) =>
            Error.NotFound("trainer.question.not.found", $"Вопрос {questionId} не найден.");

        public static Error StemRequired() =>
            Error.Validation("trainer.question.stem.required", "Текст вопроса обязателен.");

        public static Error OptionsRequired() =>
            Error.Validation("trainer.question.options.required", "Вопрос с выбором требует минимум два варианта ответа.");

        public static Error SingleChoiceOneCorrect() =>
            Error.Validation("trainer.question.single.one.correct", "Вопрос с одним ответом требует ровно один правильный вариант.");

        public static Error MultiChoiceNeedsCorrect() =>
            Error.Validation("trainer.question.multi.needs.correct", "Вопрос с несколькими ответами требует хотя бы один правильный вариант.");

        public static Error ReferenceRequired() =>
            Error.Validation("trainer.question.reference.required", "Вопрос с точным ответом требует эталонный ответ.");

        public static Error OptionsNotAllowed() =>
            Error.Validation("trainer.question.options.not.allowed", "Текстовый вопрос не должен иметь вариантов ответа.");

        public static Error InvalidType(string raw) =>
            Error.Validation("trainer.question.invalid.type", $"Недопустимый тип вопроса: «{raw}». Ожидается SINGLE_CHOICE, MULTI_CHOICE, EXACT_TEXT или OPEN_TEXT.");
    }

    public static class Session
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("trainer.session.not.found", $"Сессия {id} не найдена.");

        public static Error AlreadyCompleted() =>
            Error.Validation("trainer.session.already.completed", "Сессия уже завершена.");

        public static Error NotInProgress() =>
            Error.Validation("trainer.session.not.in.progress", "Действие доступно только для активной сессии.");

        public static Error ItemNotFound(Guid itemId) =>
            Error.NotFound("trainer.session.item.not.found", $"Вопрос {itemId} не найден в сессии.");

        public static Error NoTopics() =>
            Error.Validation("trainer.session.no.topics", "Сессия должна включать хотя бы одну тему.");

        public static Error InvalidMode(string raw) =>
            Error.Validation("trainer.session.invalid.mode", $"Недопустимый режим: «{raw}». Ожидается DRILL.");

        public static Error InvalidRevealPolicy(string raw) =>
            Error.Validation("trainer.session.invalid.reveal.policy", $"Недопустимая политика раскрытия: «{raw}». Ожидается END_OF_SESSION или PER_QUESTION.");

        public static Error LearnNoQuestions() =>
            Error.Validation("trainer.learn.no.questions", "Для этой темы нет доступных вопросов для обучения по тестам.");

        public static Error ReviewNoQuestions() =>
            Error.Validation("trainer.review.no.questions", "Нет доступных вопросов для теста по выбранному набору.");

        public static Error AnswerAlreadyChecked() =>
            Error.Conflict("trainer.session.answer.already.checked", "Ответ на этот вопрос уже проверен.");

        public static Error AnswerNotOpenText() =>
            Error.Validation("trainer.answer.not_open_text", "Голосовой ответ доступен только для открытых вопросов.");

        public static Error NoQuestions() =>
            Error.Validation("trainer.session.no.questions", "Не удалось собрать вопросы для сессии.");

        public static Error MockNoQuestions() =>
            Error.Validation("trainer.mock.no.questions", "По этому треку нет доступных вопросов для симуляции собеседования.");
    }

    public static class Transcribe
    {
        public static Error InvalidAudio(string detail) =>
            Error.Validation("trainer.transcribe.invalid_audio", $"Некорректный аудио-файл: {detail}");

        public static Error Failed() =>
            Error.Failure("trainer.transcribe.failed", "Не удалось распознать аудио. Попробуйте ещё раз.").AsTransient();

        /// <summary>
        ///     Аудио-файл превышает серверный байтовый лимит (#614 C2 / #663) — proxy на длительность,
        ///     отбивается ДО транскрипции, чтобы не тратить STT-бюджет. Сообщение явно называет предел
        ///     длительности (<paramref name="maxMinutes"/> мин), чтобы пользователь понял, что делать.
        /// </summary>
        public static Error TooLong(int maxMinutes) =>
            Error.Validation(
                "trainer.transcribe.too_long",
                $"Запись слишком большая. Максимальная длительность ответа — {maxMinutes} мин. Запишите короче и попробуйте снова.");
    }

    public static class Mastery
    {
        public static Error NotFound(Guid userId, Guid topicId) =>
            Error.NotFound("trainer.mastery.not.found", $"Прогресс по теме {topicId} для пользователя {userId} не найден.");

        public static Error InvalidScore() =>
            Error.Validation("trainer.mastery.invalid.score", "Оценка должна быть в диапазоне 0..100.");
    }

    public static class Bookmark
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("trainer.bookmark.not.found", $"Закладка {id} не найдена.");

        public static Error AlreadyExists() =>
            Error.Conflict("trainer.bookmark.already.exists", "Закладка на этот вопрос уже существует.");
    }

    public static class FeedbackRating
    {
        public static Error NotFound() =>
            Error.NotFound("trainer.feedback_rating.not.found", "Оценка AI-разбора не найдена.");

        public static Error InvalidRating(string raw) =>
            Error.Validation("trainer.feedback_rating.invalid", $"Недопустимая оценка: «{raw}». Ожидается UP или DOWN.");

        /// <summary>
        ///     Оценить можно только AI-разбор отвеченного открытого вопроса (OPEN_TEXT с выставленным
        ///     вердиктом — не PENDING). Закрытые/неотвеченные/ещё не оценённые item'ы — нечего оценивать.
        /// </summary>
        public static Error NotRateable() =>
            Error.Conflict(
                "trainer.feedback_rating.not.rateable",
                "Оценить можно только готовый AI-разбор открытого ответа.");
    }

    public static class StudyState
    {
        public static Error NotFound(Guid userId, Guid questionId) =>
            Error.NotFound("trainer.study.state.not.found", $"Состояние изучения вопроса {questionId} для пользователя {userId} не найдено.");
    }

    public static class SelfAssess
    {
        /// <summary>
        ///     Неизвестный вердикт мягкой самооценки (#691 t8). Пока допустим только <c>UNSURE</c>
        ///     («Не уверен» → REVIEW). Маппится на 400.
        /// </summary>
        public static Error InvalidVerdict(string raw) =>
            Error.Validation("trainer.self_assess.invalid", $"Недопустимая самооценка: «{raw}». Ожидается UNSURE.");

        /// <summary>
        ///     «Не уверен» — действие ДО ответа (#691). На уже отвеченном вопросе (<c>AnsweredAt != null</c>)
        ///     самооценка запрещена: иначе оценённый вопрос форсился бы обратно в REVIEW (нарушение
        ///     целостности данных). Маппится на 409.
        /// </summary>
        public static Error AlreadyAnswered(Guid itemId) =>
            Error.Conflict(
                "trainer.self_assess.already.answered",
                $"Вопрос {itemId} уже отвечен — самооценка «Не уверен» доступна только до ответа.");
    }

    public static class Access
    {
        /// <summary>
        ///     Действие (платный банк / голосовой ответ / мок-собес) требует подписки тренажёра
        ///     (capability <c>cap:TRAINER_PRO</c>). Бесплатный tier = FREE-банки + текстовые ответы.
        /// </summary>
        public static Error ProRequired() =>
            Error.Authorization("trainer.pro.required", "Это доступно только по подписке на тренажёр (PRO).");

        public static Error OpenGradeRateLimitExceeded(int limit) =>
            Error.RateLimit(
                "trainer.open_grade.rate_limited",
                $"Слишком много AI-проверок открытых ответов. Лимит — {limit} в минуту.");

        /// <summary>
        ///     Исчерпан лимит AI-использования по измерению <paramref name="dimension"/> за период (#614 C2):
        ///     открытые AI-проверки в день / голосовые МИНУТЫ в месяц (#663) / мок-собесы в месяц.
        ///     <paramref name="limit"/> — достигнутый потолок (для VOICE — в минутах). Маппится на 403.
        /// </summary>
        public static Error QuotaExceeded(QuotaDimension dimension, int limit)
        {
            string message = dimension switch
            {
                QuotaDimension.OPEN_GRADE =>
                    $"Достигнут дневной лимит AI-проверок открытых ответов ({limit}). Попробуйте завтра или оформите PRO.",
                QuotaDimension.VOICE =>
                    $"Достигнут месячный лимит голосовых ответов ({limit} мин). Лимит обновится в начале месяца.",
                QuotaDimension.MOCK =>
                    $"Достигнут месячный лимит мок-собеседований ({limit}).",
                _ => $"Достигнут лимит AI-использования ({limit}).",
            };

            return Error.Authorization("trainer.quota.exceeded", message);
        }
    }

    public static class Integration
    {
        public static Error EducationContentUnavailable() =>
            Error.Failure("trainer.education.unavailable", "Сервис контента временно недоступен.").AsTransient();

        public static Error QuizNotFound(Guid quizId) =>
            Error.NotFound("trainer.quiz.not.found", $"Квиз банка {quizId} не найден в сервисе контента.");
    }
}
