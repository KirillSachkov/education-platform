using AuthService.Domain.ValueObjects;

namespace AuthService.Domain;

/// <summary>Расширенный профиль пользователя платформы. Id = Account.Id.</summary>
public sealed record UserProfile
{
    /// <summary>UUID пользователя (FK к Account.Id).</summary>
    public required Guid Id { get; init; }

    /// <summary>Краткое описание / биография пользователя.</summary>
    public Bio? Bio { get; set; }

    /// <summary>Дата создания профиля в UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Дата последнего обновления профиля в UTC.</summary>
    public required DateTime UpdatedAt { get; set; }

    /// <summary>Ролевые профили пользователя; <c>null</c> если роли не заполнены.</summary>
    public Profiles? Profiles { get; set; }

    /// <summary>ID файлового ассета аватара пользователя.</summary>
    public Guid? AvatarId { get; set; }

    public long AvatarBindingRevision { get; set; } = -1;

    public void UpdateAvatar(Guid? avatarId, DateTime now, long bindingRevision = 0)
    {
        AvatarId = avatarId;
        AvatarBindingRevision = avatarId is null
            ? Math.Max(AvatarBindingRevision, bindingRevision)
            : bindingRevision;
        UpdatedAt = now;
    }

    public void UpdateBaseProfile(Bio? bio, DateTime now)
    {
        Bio = bio;
        UpdatedAt = now;
    }

    public void SetGitHubUrl(GitHubUrl gitHubUrl, DateTime now)
    {
        Profiles currentProfiles = Profiles ?? new Profiles();
        StudentProfile currentStudent = currentProfiles.Student ?? new StudentProfile { UpdatedAt = now };
        Profiles = currentProfiles with
        {
            Student = currentStudent with { GitHubUrl = gitHubUrl, UpdatedAt = now }
        };
        UpdatedAt = now;
    }

    public void ClearGitHubUrl(DateTime now)
    {
        Profiles currentProfiles = Profiles ?? new Profiles();
        StudentProfile? currentStudent = currentProfiles.Student;
        if (currentStudent is null)
            return;

        Profiles = currentProfiles with
        {
            Student = currentStudent with { GitHubUrl = null, UpdatedAt = now }
        };
        UpdatedAt = now;
    }

    public void UpdateAuthorProfile(
        Specialization? specialization,
        AboutAsAuthor? aboutAsAuthor,
        DateTime now)
    {
        Profiles currentProfiles = Profiles ?? new Profiles();
        Profiles = currentProfiles with
        {
            Author = new AuthorProfile
            {
                Specialization = specialization,
                AboutAsAuthor = aboutAsAuthor,
                UpdatedAt = now
            }
        };

        UpdatedAt = now;
    }

    public void UpdateReviewerProfile(
        int? reviewCapacity,
        Expertise? expertise,
        DateTime now)
    {
        Profiles currentProfiles = Profiles ?? new Profiles();
        Profiles = currentProfiles with
        {
            Reviewer = new ReviewerProfile
            {
                ReviewCapacity = reviewCapacity,
                Expertise = expertise,
                UpdatedAt = now
            }
        };

        UpdatedAt = now;
    }
}
