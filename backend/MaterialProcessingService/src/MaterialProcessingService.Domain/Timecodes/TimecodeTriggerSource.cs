namespace MaterialProcessingService.Domain.Timecodes;

/// <summary>
///     Как был запущен job обработки видео. <c>MANUAL</c> — автор нажал кнопку
///     «AI-обработка видео» (ручной enqueue, проходит auth+ownership+rate-limit).
///     <c>AUTO</c> — запущен реактивно, когда FileService сообщил о готовности видео
///     (<c>VideoReadyForProcessing</c>): без rate-limit'а, system-контекст. Только
///     AUTO-job'ы при падении публикуют <c>VideoAutoProcessingFailed</c> → in-app
///     уведомление владельцу. Issue #648.
/// </summary>
public enum TimecodeTriggerSource
{
    MANUAL,
    AUTO,
}
