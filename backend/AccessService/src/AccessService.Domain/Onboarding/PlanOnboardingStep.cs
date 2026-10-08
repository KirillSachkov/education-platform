using Ordering;

namespace AccessService.Domain.Onboarding;

/// <summary>
///     Шаг онбординга. MARKDOWN-шаги имеют title+body (автор-редактируемые).
///     Авто-шаги (TG/GH/NOTIF) имеют только тип + sort order.
/// </summary>
public sealed class PlanOnboardingStep
{
    public const int TITLE_MAX_LENGTH = 200;
    public const int BODY_MAX_LENGTH = 50_000;

    private PlanOnboardingStep() { } // EF

    private PlanOnboardingStep(
        Guid id,
        Guid planId,
        PlanOnboardingStepType type,
        bool isSkippable,
        SortKey sortOrder,
        string? title,
        string? body,
        DateTimeOffset createdAt)
    {
        Id = id;
        PlanId = planId;
        Type = type;
        IsSkippable = isSkippable;
        SortOrder = sortOrder;
        Title = title;
        Body = body;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public PlanOnboardingStepType Type { get; private set; }

    public bool IsSkippable { get; private set; }

    public SortKey SortOrder { get; private set; } = null!;

    public string? Title { get; private set; }

    public string? Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal static Result<PlanOnboardingStep, Error> CreateMarkdown(
        Guid planId,
        string title,
        string body,
        bool isSkippable,
        SortKey sortOrder,
        DateTimeOffset now)
    {
        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure) return titleResult.Error;

        Result<string, Error> bodyResult = ValidateBody(body);
        if (bodyResult.IsFailure) return bodyResult.Error;

        // Id=Guid.Empty — EF выставит через TimeOrderedGuidValueGenerator на Add
        // (config: PlanOnboardingStepConfiguration). Set Id вручную здесь сломал
        // бы change tracker (entity → state Modified, не Added).
        return new PlanOnboardingStep(
            Guid.Empty,
            planId,
            PlanOnboardingStepType.MARKDOWN,
            isSkippable,
            sortOrder,
            titleResult.Value,
            bodyResult.Value,
            now);
    }

    internal static PlanOnboardingStep CreateAuto(
        Guid planId,
        PlanOnboardingStepType type,
        SortKey sortOrder,
        DateTimeOffset now)
    {
        if (type == PlanOnboardingStepType.MARKDOWN)
        {
            throw new ArgumentException("CreateAuto rejects MARKDOWN — use CreateMarkdown.", nameof(type));
        }

        return new PlanOnboardingStep(
            Guid.Empty,
            planId,
            type,
            isSkippable: true,
            sortOrder,
            title: null,
            body: null,
            now);
    }

    public UnitResult<Error> UpdateMarkdown(string title, string body, bool isSkippable, DateTimeOffset now)
    {
        if (Type != PlanOnboardingStepType.MARKDOWN)
        {
            return OnboardingErrors.AutoStepNotEditable();
        }

        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure) return titleResult.Error;

        Result<string, Error> bodyResult = ValidateBody(body);
        if (bodyResult.IsFailure) return bodyResult.Error;

        Title = titleResult.Value;
        Body = bodyResult.Value;
        IsSkippable = isSkippable;
        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    public void UpdateOrder(SortKey newOrder, DateTimeOffset now)
    {
        SortOrder = newOrder;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Toggle <see cref="IsSkippable"/> для любого типа шага. Markdown-шаги
    ///     меняют только этот флаг (без перезаписи title/body); auto-шаги (TG/GH/NOTIF)
    ///     по дефолту IsSkippable=true, но автор может сделать их обязательными.
    /// </summary>
    public void SetIsSkippable(bool isSkippable, DateTimeOffset now)
    {
        if (IsSkippable == isSkippable) return;
        IsSkippable = isSkippable;
        UpdatedAt = now;
    }

    private static Result<string, Error> ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return OnboardingErrors.MarkdownTitleRequired();
        }

        string trimmed = title.Trim();
        if (trimmed.Length > TITLE_MAX_LENGTH)
        {
            return OnboardingErrors.MarkdownTitleTooLong();
        }

        return trimmed;
    }

    private static Result<string, Error> ValidateBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return OnboardingErrors.MarkdownBodyRequired();
        }

        if (body.Length > BODY_MAX_LENGTH)
        {
            return OnboardingErrors.MarkdownBodyTooLong();
        }

        return body;
    }
}
