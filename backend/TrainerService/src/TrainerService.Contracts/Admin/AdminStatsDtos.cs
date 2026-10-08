using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Admin;

// === GET /trainer/admin/stats?days=N ===

/// <summary>
///     Композитный admin-снимок тренажёра за окно <c>days</c> (#614 D1): AI-расходы (из лоджера
///     <c>trainer.ai_usage</c>) + использование (из <c>trainer.training_sessions</c>). Только AI-СТОИМОСТЬ
///     и активность — выручка/маржа считаются на стороне AccessService (заказы/гранты SUBSCRIPTION-плана) и
///     линкуются в D2-фронте. Стоимость отдаётся в двух единицах: <see cref="AdminAiSpendDto.TotalCostMicroRub"/>
///     (микрорубли, ₽×1 000 000 — источник правды без потери копеек) и <see cref="AdminAiSpendDto.TotalCostRub"/>
///     (рубли = micro/1e6, для удобного показа).
/// </summary>
public sealed record AdminStatsDto(
    int Days,
    AdminAiSpendDto AiSpend,
    AdminUsageDto Usage);

// --- AI spend ---

/// <summary>
///     AI-расходы за окно: суммарная стоимость (микрорубли + рубли), число операций и токенов, разбивки
///     по операции и модели, плотный дневной ряд стоимости и топ пользователей по тратам.
/// </summary>
/// <param name="TotalCostMicroRub">Суммарная стоимость в микрорублях (₽×1 000 000) — источник правды.</param>
/// <param name="TotalCostRub">Та же сумма в рублях (= micro/1e6) — для показа.</param>
/// <param name="TotalOperations">Сколько AI-вызовов записано в окне (строк лоджера).</param>
/// <param name="TotalInputTokens">Сумма входных токенов (null-токены STT не считаются).</param>
/// <param name="TotalOutputTokens">Сумма выходных токенов.</param>
/// <param name="ByOperation">Разбивка по типу операции (OPEN_ANSWER_GRADE/MOCK_AGGREGATE/TRANSCRIPTION).</param>
/// <param name="ByModel">Разбивка по модели (с токенами — видно, где дорого).</param>
/// <param name="Daily">Плотный дневной ряд стоимости (zero-filled, каждый день окна присутствует).</param>
/// <param name="TopUsers">Топ-10 пользователей по тратам (убыв.), обогащённые именем + аватаром из AuthService.</param>
/// <param name="Money">Производные «деньги»-метрики: стоимость на пользователя / сессию / проверку + проекция на месяц.</param>
public sealed record AdminAiSpendDto(
    long TotalCostMicroRub,
    decimal TotalCostRub,
    long TotalOperations,
    long TotalInputTokens,
    long TotalOutputTokens,
    IReadOnlyList<AdminAiOperationBreakdownDto> ByOperation,
    IReadOnlyList<AdminAiModelBreakdownDto> ByModel,
    IReadOnlyList<AdminAiDailyPointDto> Daily,
    IReadOnlyList<AdminAiTopUserDto> TopUsers,
    AdminAiMoneyMetricsDto Money);

/// <summary>
///     Производные «деньги»-метрики AI-расходов окна (#681 / #680): средняя стоимость на одного
///     пользователя (по distinct юзерам ai_usage окна), на одну начатую сессию, на одну проверку
///     открытого ответа (OPEN_ANSWER_GRADE), и проекция расхода на 30 дней (дневной средний окна × 30).
///     Каждая — в микрорублях (источник правды, ₽×1 000 000) + рублях (для показа). Нулевой знаменатель ⇒ метрика 0.
/// </summary>
/// <param name="CostPerUserMicroRub">Суммарная стоимость / число distinct пользователей в окне (микрорубли).</param>
/// <param name="CostPerUserRub">То же в рублях.</param>
/// <param name="CostPerSessionMicroRub">Суммарная стоимость / число начатых сессий в окне (микрорубли).</param>
/// <param name="CostPerSessionRub">То же в рублях.</param>
/// <param name="CostPerGradeMicroRub">Суммарная стоимость / число операций OPEN_ANSWER_GRADE в окне (микрорубли).</param>
/// <param name="CostPerGradeRub">То же в рублях.</param>
/// <param name="ProjectedMonthMicroRub">Проекция на 30 дней: (суммарная стоимость / days) × 30 (микрорубли).</param>
/// <param name="ProjectedMonthRub">То же в рублях.</param>
public sealed record AdminAiMoneyMetricsDto(
    long CostPerUserMicroRub,
    decimal CostPerUserRub,
    long CostPerSessionMicroRub,
    decimal CostPerSessionRub,
    long CostPerGradeMicroRub,
    decimal CostPerGradeRub,
    long ProjectedMonthMicroRub,
    decimal ProjectedMonthRub);

/// <summary>Стоимость + число вызовов по одному типу операции.</summary>
public sealed record AdminAiOperationBreakdownDto(
    string Operation,
    long Count,
    long CostMicroRub);

/// <summary>Стоимость + токены + число вызовов по одной модели.</summary>
public sealed record AdminAiModelBreakdownDto(
    string Model,
    long Count,
    long CostMicroRub,
    long InputTokens,
    long OutputTokens);

/// <summary>Один календарный день (UTC) ряда: суммарная стоимость в микрорублях. Плотный (нулевые дни заполнены).</summary>
public sealed record AdminAiDailyPointDto(
    DateOnly Date,
    long CostMicroRub);

/// <summary>
///     Один пользователь из топа по тратам: id, сколько потратил (микрорубли), сколько вызовов сделал,
///     плюс отображаемое имя и URL аватара (best-effort из AuthService — оба <c>null</c>, если не
///     резолвится: AuthService недоступен или у юзера нет профиля/аватара — фронт показывает id).
/// </summary>
public sealed record AdminAiTopUserDto(
    Guid UserId,
    long CostMicroRub,
    long OperationCount,
    string? DisplayName,
    string? AvatarUrl);

// --- Usage ---

/// <summary>
///     Использование тренажёра за окно: сколько сессий начато (всего + по режиму), сколько уникальных
///     активных пользователей, сколько сессий завершено.
/// </summary>
/// <param name="SessionsStarted">Всего сессий, начатых в окне (по <c>started_at</c>).</param>
/// <param name="ByMode">Разбивка начатых сессий по режиму (DRILL/LEARN/MOCK).</param>
/// <param name="ActiveUsers">Различных пользователей, начавших ≥1 сессию в окне.</param>
/// <param name="CompletedSessions">Сколько из начатых в окне сессий уже завершены (status=COMPLETED).</param>
public sealed record AdminUsageDto(
    long SessionsStarted,
    IReadOnlyList<AdminUsageModeBreakdownDto> ByMode,
    long ActiveUsers,
    long CompletedSessions);

/// <summary>Сколько сессий начато в окне по одному режиму.</summary>
public sealed record AdminUsageModeBreakdownDto(
    string Mode,
    long Count);
