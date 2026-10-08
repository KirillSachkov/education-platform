using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.AI;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Configuration;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.UseCases;

/// <summary>
///     Голосовой ответ на открытый вопрос (#585, NEW-дизайн): студент НЕ редактирует распознанный
///     текст — только записывает и отправляет. Бэк транскрибирует аудио (Whisper STT) и записывает
///     распознанный текст как ответ на item — ровно тем же путём (<see cref="SessionAnswerGrading"/>),
///     что и печатный OPEN_TEXT-ответ в <c>CheckAnswer</c>. Развилка по режиму сессии:
///     <list type="bullet">
///         <item><b>LEARN/DRILL (тренировка/тест):</b> голос грейдится AI <b>инлайн</b> — вердикт +
///             балл + фидбэк, mastery + study-state, раскрытие под PER_QUESTION. Распознанный текст
///             возвращается как «Твой ответ» (<c>AnswerText</c>), чтобы разбор не был пустым.</item>
///         <item><b>MOCK (экзамен):</b> ответ записывается PENDING (ключ скрыт), реальную оценку даёт
///             отложенный <c>MockAnswerGradingService</c> после Complete.</item>
///     </list>
///     Промпт грейдера толерантен к STT-ошибкам (искажённые термины не снижают балл).
/// </summary>
public sealed record SubmitVoiceAnswerCommand(
    Guid SessionId,
    Guid ItemId,
    Guid UserId,
    bool IsAdmin,
    string FileName,
    string ContentType,
    long Length,
    IReadOnlyList<byte> AudioBytes) : ICommand;

public sealed class SubmitVoiceAnswerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/sessions/{sessionId:guid}/answers/{itemId:guid}/voice",
                async Task<EndpointResult<CheckAnswerResponse>> (
                    Guid sessionId,
                    Guid itemId,
                    IFormFile audio,
                    SubmitVoiceAnswerHandler handler,
                    IOptions<TrainerAiOptions> options,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                {
                    if (audio.Length > SubmitVoiceAnswerHandler.MAX_AUDIO_BYTES)
                    {
                        return Result.Failure<CheckAnswerResponse, Error>(
                            TrainerServiceErrors.Transcribe.InvalidAudio("файл больше 25 МБ"));
                    }

                    if (audio.Length > options.Value.MaxAudioBytes)
                    {
                        int maxMinutes = (options.Value.MaxVoiceAnswerSeconds + 59) / 60;
                        return Result.Failure<CheckAnswerResponse, Error>(
                            TrainerServiceErrors.Transcribe.TooLong(maxMinutes));
                    }

                    await using Stream stream = audio.OpenReadStream();
                    using MemoryStream buffer = new();
                    await stream.CopyToAsync(buffer, cancellationToken);

                    return await handler.Handle(
                        new SubmitVoiceAnswerCommand(
                            sessionId,
                            itemId,
                            user.UserId,
                            user.IsAdmin,
                            audio.FileName,
                            audio.ContentType ?? string.Empty,
                            audio.Length,
                            buffer.ToArray()),
                        cancellationToken);
                })
            .RequirePermissions(PlatformPermissions.Content.VIEW)
            .RequireRateLimiting("trainer-transcribe")
            .DisableAntiforgery();
    }
}

public sealed class SubmitVoiceAnswerHandler : ICommandHandler<CheckAnswerResponse, SubmitVoiceAnswerCommand>
{
    /// <summary>OpenAI Whisper per-file limit — 25 MB.</summary>
    public const long MAX_AUDIO_BYTES = 25 * 1024 * 1024;

    private readonly ITrainingSessionsRepository _sessions;
    private readonly IAiTranscriptionClient _transcription;
    private readonly IEntitlementChecker _entitlements;
    private readonly TrainerQuotaService _quota;
    private readonly SessionAnswerGrading _grading;
    private readonly AiUsageLedger _aiUsageLedger;
    private readonly IOptions<TrainerAiOptions> _options;
    private readonly ITransactionManager _transactions;

    public SubmitVoiceAnswerHandler(
        ITrainingSessionsRepository sessions,
        IAiTranscriptionClient transcription,
        IEntitlementChecker entitlements,
        TrainerQuotaService quota,
        SessionAnswerGrading grading,
        AiUsageLedger aiUsageLedger,
        IOptions<TrainerAiOptions> options,
        ITransactionManager transactions)
    {
        _sessions = sessions;
        _transcription = transcription;
        _entitlements = entitlements;
        _quota = quota;
        _grading = grading;
        _aiUsageLedger = aiUsageLedger;
        _options = options;
        _transactions = transactions;
    }

    public async Task<Result<CheckAnswerResponse, Error>> Handle(
        SubmitVoiceAnswerCommand command,
        CancellationToken cancellationToken)
    {
        // Голосовой ответ — PRO-фича (#614): бесплатный tier = только текстовые ответы. Гейтим до
        // загрузки/STT (не тратим бюджет Whisper на не-PRO). Admin bypass внутри хелпера.
        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            command.UserId, command.IsAdmin, _entitlements, cancellationToken);
        if (!hasPro)
            return TrainerServiceErrors.Access.ProRequired();

        // Own-data: only the session owner may submit a voice answer into it. Foreign session → 404.
        Result<TrainingSession, Error> sessionResult = await _sessions.GetWithItemsAsync(
            s => s.Id == command.SessionId && s.UserId == command.UserId,
            cancellationToken);
        if (sessionResult.IsFailure)
            return TrainerServiceErrors.Session.NotFound(command.SessionId);

        TrainingSession session = sessionResult.Value;

        TrainingSessionItem? item = session.Items.FirstOrDefault(i => i.Id == command.ItemId);
        if (item is null)
            return TrainerServiceErrors.Session.ItemNotFound(command.ItemId);

        // Voice answers are only for open questions — a choice/exact question is auto-graded and
        // makes no sense to dictate. Reject before spending STT budget.
        if (!string.Equals(item.QuestionType, AnswerGrader.OPEN_TEXT, StringComparison.Ordinal))
            return TrainerServiceErrors.Session.AnswerNotOpenText();

        // Idempotency mirrors CheckAnswer: an already-answered item is not re-answered → 409.
        if (item.AnsweredAt is not null)
            return TrainerServiceErrors.Session.AnswerAlreadyChecked();

        if (command.Length <= 0 || command.AudioBytes.Count == 0)
            return TrainerServiceErrors.Transcribe.InvalidAudio("пустой файл");

        if (command.Length > MAX_AUDIO_BYTES || command.AudioBytes.Count > MAX_AUDIO_BYTES)
            return TrainerServiceErrors.Transcribe.InvalidAudio("файл больше 25 МБ");

        // Tighter server-side byte cap (#614 C2) as a coarse duration proxy — rejected BEFORE transcription
        // so an oversized recording can't burn STT budget. Sits on top of the absolute 25 MiB Whisper limit.
        // The user-facing 3-min cap is enforced primarily client-side (the recorder auto-stops); this is the
        // backstop, and the message names the duration limit (#663).
        long maxAudioBytes = _options.Value.MaxAudioBytes;
        int maxMinutes = (_options.Value.MaxVoiceAnswerSeconds + 59) / 60;
        if (command.Length > maxAudioBytes || command.AudioBytes.Count > maxAudioBytes)
            return TrainerServiceErrors.Transcribe.TooLong(maxMinutes);

        if (!command.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return TrainerServiceErrors.Transcribe.InvalidAudio("ожидается аудио-файл");

        // Reserve one maximum-length answer atomically before the billable STT call. This closes the
        // check-then-consume race: parallel uploads cannot all pass on the same remaining budget.
        Result<VoiceQuotaReservation, Error> reservationResult = await _quota.TryReserveVoiceAsync(
            command.UserId,
            hasPro,
            _options.Value.MaxVoiceAnswerSeconds,
            cancellationToken);
        if (reservationResult.IsFailure)
            return reservationResult.Error;
        VoiceQuotaReservation reservation = reservationResult.Value;

        AiTranscriptionRequest request = new(
            Model: _options.Value.Transcription.Model,
            FileName: command.FileName,
            ContentType: command.ContentType,
            AudioBytes: command.AudioBytes,
            LanguageHint: "ru");

        Result<AiTranscriptionResult, Error> result;
        try
        {
            result = await _transcription.TranscribeAsync(request, cancellationToken);
        }
        catch
        {
            await _quota.ReleaseVoiceReservationAsync(reservation);
            throw;
        }

        if (result.IsFailure)
        {
            await _quota.ReleaseVoiceReservationAsync(reservation);
            return TrainerServiceErrors.Transcribe.Failed();
        }

        string transcript = result.Value.FullText;

        double durationSeconds = result.Value.DurationSeconds;
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            // The STT call succeeded and can be billable. Keep the full reservation when its actual
            // duration is unavailable so repeated malformed provider responses cannot bypass the quota.
            return TrainerServiceErrors.Transcribe.Failed();
        }
        int billableSeconds = Math.Max(0, (int)Math.Ceiling(durationSeconds));

        // Duration is authoritative only after STT. Keep the full reservation on an overlong recording
        // so repeated compressed uploads cannot bypass the cost cap, but never save the answer itself.
        if (durationSeconds > _options.Value.MaxVoiceAnswerSeconds)
        {
            await _aiUsageLedger.RecordTranscriptionAsync(
                command.UserId, result.Value.Model, durationSeconds, session.Id, cancellationToken);
            return TrainerServiceErrors.Transcribe.TooLong(maxMinutes);
        }

        await _quota.CommitVoiceReservationAsync(reservation, billableSeconds, CancellationToken.None);

        // Record the billable STT call even if a later answer-grade/database step fails.
        await _aiUsageLedger.RecordTranscriptionAsync(
            command.UserId, result.Value.Model, durationSeconds, session.Id, cancellationToken);

        CheckAnswerResponse response;
        // AI usage of the inline open-answer grade (#614 C1) — recorded after the answer is persisted.
        // Set only on the non-MOCK inline-grade path (a real LLM grade ran); null/empty otherwise.
        AiUsage? gradeUsage = null;
        string gradeModel = string.Empty;

        if (session.Mode == TrainingMode.MOCK)
        {
            // MOCK (экзамен): открытый ответ НЕ грейдится инлайн — записываем распознанный текст как
            // PENDING-ответ, реальную оценку даст фоновый MockAnswerGradingService после Complete.
            // Пустой транскрипт записывается как пустой ответ (фоновый грейдер посчитает «нет ответа»).
            string? answerRaw = string.IsNullOrWhiteSpace(transcript) ? null : transcript;
            Result<CheckAnswerResponse, Error> recordResult = OpenAnswerRecorder.Record(session, item, answerRaw);
            if (recordResult.IsFailure)
                return recordResult.Error;
            response = recordResult.Value;
        }
        else
        {
            // LEARN/DRILL («тренировка»/«тест»): голосовой ответ грейдится AI инлайн — ровно так же, как
            // печатный OPEN_TEXT в CheckAnswer (вердикт + балл + фидбэк, mastery + study-state, раскрытие
            // под PER_QUESTION). Распознанный текст возвращается как «Твой ответ» (AnswerText). Голосовой
            // OPEN_GRADE не списывает отдельную OPEN_GRADE-квоту — VOICE-квота уже покрыла весь конвейер.
            Result<InlineOpenGradeResult, Error> graded = await _grading.GradeAndRecordOpenInlineAsync(
                session, item, command.UserId, transcript, cancellationToken);
            if (graded.IsFailure)
                return graded.Error;
            response = graded.Value.Response;
            gradeUsage = graded.Value.Usage;
            gradeModel = graded.Value.Model;
        }

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Plus the inline open-answer LLM grade (non-MOCK), if one actually ran. A real LLM call sets the
        // model (empty-answer / fallback → no call → nothing to bill). Best-effort, never fails the save.
        if (!string.IsNullOrEmpty(gradeModel))
        {
            await _aiUsageLedger.RecordLlmAsync(
                command.UserId,
                AiUsageOperation.OPEN_ANSWER_GRADE,
                gradeModel,
                gradeUsage,
                session.Id,
                cancellationToken);
        }

        return response;
    }
}
