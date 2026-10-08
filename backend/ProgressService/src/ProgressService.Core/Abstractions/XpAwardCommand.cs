using ProgressService.Domain.Gamification;

namespace ProgressService.Core.Abstractions;

/// <summary>
/// Команда на начисление XP. Идемпотентна по паре <c>(UserId, AwardType, SourceId)</c>.
/// <para>
/// <b>UserId</b> задаётся явно — сервис не резолвит его. Для курсовых наград (issue, module,
/// project) указывается <c>EnrollmentId</c>, для user-scoped (material view) — null.
/// </para>
/// </summary>
public sealed record XpAwardCommand(
    /// <summary>
    /// Пользователь, которому начисляется XP. Для курсовых наград можно передать
    /// <see cref="Guid.Empty"/> — сервис резолвит пользователя через <c>EnrollmentId</c>.
    /// </summary>
    Guid UserId,
    /// <summary>
    /// Идентификатор enrollment, если награда привязана к курсу (issue, module, project);
    /// null для user-scoped наград (material view).
    /// </summary>
    Guid? EnrollmentId,
    /// <summary>Тип награды, определяющий причину и размер начисления XP.</summary>
    XpAwardType AwardType,
    /// <summary>Идентификатор бизнес-источника награды для идемпотентности.</summary>
    Guid SourceId);
