using ProgressService.Domain.IssueSubmissions;

namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

/// <summary>
///     PR-URL валидация при сдаче (#718). Не-PR github.com-ссылка (страница «создать PR»,
///     ветка, голый репозиторий) должна отвергаться, иначе ARS не создаёт AiReview и автор
///     видит пустой AI-статус. sachkov-learn.net и self-check-путь остаются свободной формой.
/// </summary>
public class IssueSubmissionPayloadTests
{
    [Theory]
    [InlineData("https://github.com/wolonee/DirectoryService/pull/new/DS-F15")] // страница «создать PR» — реальный баг #718
    [InlineData("https://github.com/owner/repo/tree/main")]                     // ветка, не PR
    [InlineData("https://github.com/owner/repo")]                               // голый репозиторий
    [InlineData("https://github.com/owner/repo/pull")]                          // /pull без номера
    [InlineData("https://github.com/owner/repo/pull/")]                         // /pull/ без номера
    [InlineData("https://github.com/owner/repo/pull/abc")]                      // не число
    [InlineData("https://github.com/owner/repo/issues/12")]                     // issue, не PR
    [InlineData("https://gist.github.com/owner/repo/pull/12")]                  // поддомен github.com — не настоящий PR
    public void Create_RejectsNonPullRequestGitHubUrl(string url)
    {
        var result = IssueSubmissionPayload.Create(url);

        Assert.True(result.IsFailure);
        Assert.Equal("issue.submission.not_pull_request", result.Error.Messages[0].Code);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/pull/123")]                                   // каноничный PR
    [InlineData("https://github.com/owner/repo/pull/1")]                                     // однозначный номер
    [InlineData("https://github.com/owner/repo/pull/123/")]                                  // trailing slash
    [InlineData("https://github.com/owner/repo/pull/123/files")]                             // sub-path
    [InlineData("https://github.com/owner/repo/pull/123?diff=split")]                        // query
    [InlineData("https://github.com/owner/repo/pull/123#pullrequestreview-4580609186")]      // fragment
    [InlineData("HTTPS://GitHub.com/Owner/Repo/pull/9")]                                     // регистр не важен
    public void Create_AcceptsValidPullRequestUrl(string url)
    {
        var result = IssueSubmissionPayload.Create(url);

        Assert.True(result.IsSuccess);
        Assert.Equal(url.Trim(), result.Value.Value);
    }

    [Theory]
    [InlineData("https://sachkov-learn.net/manual-completion")]
    [InlineData("https://sachkov-learn.net/whatever/path")]
    public void Create_DoesNotForcePullRequestFormForSachkovLearn(string url)
    {
        // sachkov-learn.net не несёт PR-контракта — PR-форма не форсится (#718).
        var result = IssueSubmissionPayload.Create(url);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_StillRejectsDisallowedDomain()
    {
        // PR-проверка не подменяет domain-allowlist — чужой домен по-прежнему отвергается своим кодом.
        var result = IssueSubmissionPayload.Create("https://gitlab.com/owner/repo/pull/1");

        Assert.True(result.IsFailure);
        Assert.Equal("issue.submission.url.domain.not.allowed", result.Error.Messages[0].Code);
    }

    [Fact]
    public void CreateSelfCheck_IgnoresPullRequestContract()
    {
        // Self-check-путь принимает свободный текст (не URL) — PR-контракта там нет.
        var result = IssueSubmissionPayload.CreateSelfCheck(
            "Я запустил решение локально и проверил критерии");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void CreateSelfCheck_AcceptsNonPullRequestGitHubUrl()
    {
        // Даже github.com-ссылка в self-check-режиме не обязана быть PR — это свободный текст.
        var result = IssueSubmissionPayload.CreateSelfCheck("https://github.com/owner/repo/tree/main");

        Assert.True(result.IsSuccess);
    }
}
