using System.Text.RegularExpressions;
using SharedKernel.DomainEvents;
using TrainerService.Domain.Topics.Events;

namespace TrainerService.Domain.Topics;

/// <summary>
/// Aggregate root: тема тренажёра — атом контент-модели. Несёт маппинг на курс
/// для CTA «подтянуть» (как level-test секции → курсы). Банки вопросов (TopicBank)
/// и mastery (TopicMastery) ссылаются на тему по Id.
/// </summary>
public sealed class Topic : AggregateRoot
{
    public const int SLUG_MAX_LENGTH = 200;
    public const int TITLE_MAX_LENGTH = 200;
    public const int AREA_MAX_LENGTH = 100;
    public const int DESCRIPTION_MAX_LENGTH = 2000;

    private static readonly Regex SlugRegex = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private Topic() { } // EF

    private Topic(
        Guid id,
        Guid trackId,
        string slug,
        string title,
        string area,
        string? description,
        TopicDirection? direction,
        string sortKey)
    {
        Id = id;
        TrackId = trackId;
        Slug = slug;
        Title = title;
        Area = area;
        Description = description;
        Direction = direction;
        SortKey = sortKey;
        IsPublished = false;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Трек (язык/стек), к которому относится тема. Required для новых тем.</summary>
    public Guid TrackId { get; private set; }

    public string Slug { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Area { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>Facet направления внутри трека (BACKEND/FRONTEND/FULLSTACK/GENERAL). Опционально.</summary>
    public TopicDirection? Direction { get; private set; }

    public Guid? RecommendedCourseId { get; private set; }

    public Guid? FallbackCourseId { get; private set; }

    public string SortKey { get; private set; } = null!;

    public bool IsPublished { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<Topic, Error> Create(
        Guid trackId,
        string? slug,
        string? title,
        string? area,
        string? description,
        TopicDirection? direction,
        string sortKey)
    {
        if (trackId == Guid.Empty)
            return TrainerServiceErrors.Topic.TrackRequired();

        Result<string, Error> slugResult = ValidateSlug(slug);
        if (slugResult.IsFailure)
            return slugResult.Error;

        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Result<string, Error> areaResult = ValidateArea(area);
        if (areaResult.IsFailure)
            return areaResult.Error;

        Result<string?, Error> descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
            return descriptionResult.Error;

        Topic topic = new(
            Guid.CreateVersion7(),
            trackId,
            slugResult.Value,
            titleResult.Value,
            areaResult.Value,
            descriptionResult.Value,
            direction,
            sortKey);

        topic.RaiseDomainEvent(new TopicCreatedEvent(topic.Id, topic.Slug));
        return topic;
    }

    public UnitResult<Error> UpdateDetails(string? title, string? area, string? description)
    {
        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Result<string, Error> areaResult = ValidateArea(area);
        if (areaResult.IsFailure)
            return areaResult.Error;

        Result<string?, Error> descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
            return descriptionResult.Error;

        Title = titleResult.Value;
        Area = areaResult.Value;
        Description = descriptionResult.Value;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public void SetRecommendedCourses(Guid? recommendedCourseId, Guid? fallbackCourseId)
    {
        RecommendedCourseId = recommendedCourseId;
        FallbackCourseId = fallbackCourseId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Переназначает тему на трек и направление (admin/seed).</summary>
    public UnitResult<Error> SetTrackAndDirection(Guid trackId, TopicDirection? direction)
    {
        if (trackId == Guid.Empty)
            return TrainerServiceErrors.Topic.TrackRequired();

        TrackId = trackId;
        Direction = direction;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public void Publish()
    {
        if (IsPublished)
            return;

        IsPublished = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new TopicPublishedEvent(Id));
    }

    public void Unpublish()
    {
        if (!IsPublished)
            return;

        IsPublished = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new TopicUnpublishedEvent(Id));
    }

    private static Result<string, Error> ValidateSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TrainerServiceErrors.Topic.SlugRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > SLUG_MAX_LENGTH)
            return TrainerServiceErrors.Topic.SlugTooLong(SLUG_MAX_LENGTH);

        if (!SlugRegex.IsMatch(trimmed))
            return TrainerServiceErrors.Topic.SlugInvalid();

        return trimmed;
    }

    private static Result<string, Error> ValidateTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TrainerServiceErrors.Topic.TitleRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > TITLE_MAX_LENGTH)
            return TrainerServiceErrors.Topic.TitleTooLong(TITLE_MAX_LENGTH);

        return trimmed;
    }

    private static Result<string, Error> ValidateArea(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TrainerServiceErrors.Topic.AreaRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > AREA_MAX_LENGTH)
            return TrainerServiceErrors.Topic.AreaTooLong(AREA_MAX_LENGTH);

        return trimmed;
    }

    private static Result<string?, Error> ValidateDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string?)null;

        string trimmed = raw.Trim();
        if (trimmed.Length > DESCRIPTION_MAX_LENGTH)
            return TrainerServiceErrors.Topic.DescriptionTooLong(DESCRIPTION_MAX_LENGTH);

        return trimmed;
    }
}
