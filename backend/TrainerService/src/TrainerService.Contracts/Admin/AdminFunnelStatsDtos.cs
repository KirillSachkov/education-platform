using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Admin;

// === GET /trainer/admin/stats/funnel?days=N (#681 T3) ===

/// <summary>
///     Admin-снимок ВОРОНКИ тренажёра за окно <c>days</c> (clamp 1..365, default 30): доля
///     старт→завершение (всего + по режиму), drop-off по позиции вопроса (на какой ординальной
///     позиции пользователи перестают отвечать) и брошенные мок-собесы. Источник —
///     <c>trainer.training_sessions</c> + <c>trainer.training_session_items</c> (Dapper-агрегаты).
/// </summary>
public sealed record AdminFunnelStatsDto(
    int Days,
    AdminCompletionDto Completion,
    IReadOnlyList<AdminModeCompletionDto> CompletionByMode,
    IReadOnlyList<AdminDropOffPositionDto> DropOffByPosition,
    AdminAbandonedMocksDto AbandonedMocks);

/// <summary>Старт→завершение по всем режимам: начато / завершено (status=COMPLETED) / доля.</summary>
public sealed record AdminCompletionDto(long Started, long Completed, double Rate);

/// <summary>Старт→завершение по одному режиму (все три режима всегда присутствуют, нулями при отсутствии).</summary>
public sealed record AdminModeCompletionDto(string Mode, long Started, long Completed, double Rate);

/// <summary>
///     Одна ординальная позиция вопроса в сессии (<c>sort_index</c>): сколько сессий ДОШЛО до неё
///     (вопрос выдан) и сколько ОТВЕТИЛО (<c>answered_at</c> не null). Спад <c>Answered</c> по позициям
///     показывает, где пользователи бросают сессию.
/// </summary>
public sealed record AdminDropOffPositionDto(int Position, long Reached, long Answered, double AnsweredRate);

/// <summary>Брошенные мок-собесы: MOCK-сессии начато / не завершено (status≠COMPLETED) / доля брошенных.</summary>
public sealed record AdminAbandonedMocksDto(long MockStarted, long MockAbandoned, double AbandonRate);
