using System.Text.RegularExpressions;
using SharedKernel.DomainEvents;
using TrainerService.Domain.Tracks.Events;

namespace TrainerService.Domain.Tracks;

/// <summary>
/// Aggregate root: трек (язык/стек) тренажёра — верхний селектор хаба. C# / TypeScript /
/// DevOps (extensible). Темы (Topic) группируются под треком по TrackId и несут facet
/// Direction для фильтра в рамках трека. Unique Slug. DRAFT → PUBLISHED (Publish()).
/// </summary>
public sealed class Track : AggregateRoot
{
    public const int SLUG_MAX_LENGTH = 200;
    public const int TITLE_MAX_LENGTH = 200;
    public const int DESCRIPTION_MAX_LENGTH = 2000;

    private static readonly Regex SlugRegex = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private Track() { } // EF

    private Track(
        Guid id,
        string slug,
        string title,
        TrackStack stack,
        string? description,
        string sortKey)
    {
        Id = id;
        Slug = slug;
        Title = title;
        Stack = stack;
        Description = description;
        SortKey = sortKey;
        IsPublished = false;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public string Slug { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public TrackStack Stack { get; private set; }

    public string? Description { get; private set; }

    public string SortKey { get; private set; } = null!;

    public bool IsPublished { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<Track, Error> Create(
        string? slug,
        string? title,
        TrackStack stack,
        string? description,
        string sortKey)
    {
        Result<string, Error> slugResult = ValidateSlug(slug);
        if (slugResult.IsFailure)
            return slugResult.Error;

        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Result<string?, Error> descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
            return descriptionResult.Error;

        Track track = new(
            Guid.CreateVersion7(),
            slugResult.Value,
            titleResult.Value,
            stack,
            descriptionResult.Value,
            sortKey);

        track.RaiseDomainEvent(new TrackCreatedEvent(track.Id, track.Slug));
        return track;
    }

    public UnitResult<Error> UpdateDetails(string? title, TrackStack stack, string? description)
    {
        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Result<string?, Error> descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
            return descriptionResult.Error;

        Title = titleResult.Value;
        Stack = stack;
        Description = descriptionResult.Value;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public void Publish()
    {
        if (IsPublished)
            return;

        IsPublished = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new TrackPublishedEvent(Id));
    }

    public void Unpublish()
    {
        if (!IsPublished)
            return;

        IsPublished = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new TrackUnpublishedEvent(Id));
    }

    private static Result<string, Error> ValidateSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TrainerServiceErrors.Track.SlugRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > SLUG_MAX_LENGTH)
            return TrainerServiceErrors.Track.SlugTooLong(SLUG_MAX_LENGTH);

        if (!SlugRegex.IsMatch(trimmed))
            return TrainerServiceErrors.Track.SlugInvalid();

        return trimmed;
    }

    private static Result<string, Error> ValidateTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TrainerServiceErrors.Track.TitleRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > TITLE_MAX_LENGTH)
            return TrainerServiceErrors.Track.TitleTooLong(TITLE_MAX_LENGTH);

        return trimmed;
    }

    private static Result<string?, Error> ValidateDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string?)null;

        string trimmed = raw.Trim();
        if (trimmed.Length > DESCRIPTION_MAX_LENGTH)
            return TrainerServiceErrors.Track.DescriptionTooLong(DESCRIPTION_MAX_LENGTH);

        return trimmed;
    }
}
