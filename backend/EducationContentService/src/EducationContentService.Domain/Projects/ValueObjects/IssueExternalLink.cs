using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Domain.Projects.ValueObjects;

/// <summary>
///     Value Object — внешняя ссылка задачи на сторонний ресурс.
///     Позиция в списке определяется индексом в массиве (Issue.ExternalLinks).
/// </summary>
public sealed record IssueExternalLink
{
    private IssueExternalLink(Url url, Title title, bool isRequired)
    {
        Url = url;
        Title = title;
        IsRequired = isRequired;
    }

    public Url Url { get; }

    public Title Title { get; }

    public bool IsRequired { get; }

    public static Result<IssueExternalLink, Error> Create(string url, string title, bool isRequired)
    {
        Result<Url, Error> urlResult = Url.Create(url);
        if (urlResult.IsFailure)
            return urlResult.Error;

        Result<Title, Error> titleResult = Title.Create(title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        return new IssueExternalLink(urlResult.Value, titleResult.Value, isRequired);
    }
}
