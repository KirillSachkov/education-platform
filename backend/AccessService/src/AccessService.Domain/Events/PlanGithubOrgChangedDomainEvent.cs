using SharedKernel.DomainEvents;

namespace AccessService.Domain.Events;

/// <summary>
/// Поднимается, когда у плана меняется привязанный GitHub-org slug.
/// <see cref="OrgSlug"/>: непустой — установлен/изменён; <c>null</c> — отвязан.
///
/// Consumers:
/// - <c>EnsureGithubStepOnPlanGithubOrgChangedHandler</c> (#68) — на set ensure'ит GITHUB
///   step в onboarding flow, на null — удаляет.
/// - retro-grant handler (issue #65 followup) — при set должен пройтись по
///   <c>auth.user_github_orgs</c> и выдать plan-grant'ы уже-залогиненным юзерам этой org.
///   Без него юзеры получают grant только при следующем GitHub-login.
/// </summary>
public sealed record PlanGithubOrgChangedDomainEvent(Guid PlanId, Guid AuthorId, string? OrgSlug) : IDomainEvent;
