using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Infrastructure.GitHub;
using AssignmentReviewService.Infrastructure.GitHub.Models;
using AssignmentReviewService.UnitTests.Vcs.Fakes;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.GitHubApp;
using SharedKernel;

namespace AssignmentReviewService.UnitTests.Vcs;

public sealed class GitHubVcsProviderTests
{
    private const string INSTALLATION_ID = "12345";
    private const string REPO = "owner/repo";
    private const string TOKEN = "ghs_token";

    private static (GitHubVcsProvider Sut, StubHttpMessageHandler Stub, IGitHubAppTokenService Tokens) BuildSut(
        StubHttpMessageHandler stub,
        Result<string, Error>? tokenOverride = null)
    {
        IGitHubAppTokenService tokens = Substitute.For<IGitHubAppTokenService>();
        tokens.GetAppJwt().Returns(Result.Success<string, Error>("jwt"));
        tokens.GetInstallationTokenAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(tokenOverride ?? Result.Success<string, Error>(TOKEN));
        HttpClient http = new(stub) { BaseAddress = new Uri("https://api.github.com/") };
        GitHubVcsProvider sut = new(http, tokens, NullLogger<GitHubVcsProvider>.Instance);
        return (sut, stub, tokens);
    }

    [Fact]
    public async Task GetPullRequestAsync_HappyPath_ReturnsParsedPr()
    {
        const string body = """
            {
              "number": 42,
              "title": "Fix bug",
              "state": "open",
              "html_url": "https://github.com/owner/repo/pull/42",
              "user": { "login": "student-1" },
              "head": { "ref": "feature/x", "sha": "abc123" },
              "base": { "ref": "main", "sha": "def456" }
            }
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, body);
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsPullRequest, Error> result = await sut.GetPullRequestAsync(INSTALLATION_ID, REPO, 42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.Number);
        Assert.Equal("Fix bug", result.Value.Title);
        Assert.Equal("student-1", result.Value.AuthorLogin);
        Assert.Equal("abc123", result.Value.HeadSha);
        Assert.Equal("main", result.Value.BaseRef);
    }

    [Fact]
    public async Task PostCommentReplyAsync_ReviewComment_PostsToRepliesEndpoint()
    {
        // #713: reply на inline review-коммент → POST .../pulls/{n}/comments/{id}/replies.
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(
            HttpStatusCode.Created,
            """{ "id": 987, "html_url": "https://github.com/owner/repo/pull/42#discussion_r987" }""");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsPostedComment, Error> result = await sut.PostCommentReplyAsync(
            INSTALLATION_ID, REPO, 42, StudentPrMessageKind.REVIEW_COMMENT,
            inReplyToCommentId: 100, body: "Спасибо, понял!");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Messages[0].Code : "ok");
        Assert.Equal(987L, result.Value.GitHubCommentId);

        HttpRequestMessage sent = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/repos/owner/repo/pulls/42/comments/100/replies", sent.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PostCommentReplyAsync_IssueComment_PostsToIssueCommentsEndpoint()
    {
        // #713: top-level коммент PR → POST .../issues/{n}/comments (in_reply_to игнорируется).
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(
            HttpStatusCode.Created,
            """{ "id": 654, "html_url": "https://github.com/owner/repo/pull/42#issuecomment-654" }""");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsPostedComment, Error> result = await sut.PostCommentReplyAsync(
            INSTALLATION_ID, REPO, 42, StudentPrMessageKind.ISSUE_COMMENT,
            inReplyToCommentId: 0, body: "Ответ на общий вопрос.");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Messages[0].Code : "ok");
        Assert.Equal(654L, result.Value.GitHubCommentId);

        HttpRequestMessage sent = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/repos/owner/repo/issues/42/comments", sent.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetPullRequestAsync_404_ReturnsNotFoundError()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsPullRequest, Error> result = await sut.GetPullRequestAsync(INSTALLATION_ID, REPO, 42);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.pull_request.not_found", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task GetPullRequestAsync_401_InvalidatesTokenAndReturnsUnauthorized()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}");
        (GitHubVcsProvider sut, _, IGitHubAppTokenService tokens) = BuildSut(stub);

        Result<VcsPullRequest, Error> result = await sut.GetPullRequestAsync(INSTALLATION_ID, REPO, 42);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.unauthorized", result.Error.Messages[0].Code);
        tokens.Received(1).Invalidate(12345L);
    }

    [Fact]
    public async Task GetPullRequestAsync_403_DoesNotInvalidateToken()
    {
        // 403 — secondary rate limit или permission scope mismatch; токен валиден.
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.Forbidden, "{}");
        (GitHubVcsProvider sut, _, IGitHubAppTokenService tokens) = BuildSut(stub);

        Result<VcsPullRequest, Error> result = await sut.GetPullRequestAsync(INSTALLATION_ID, REPO, 42);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.unauthorized", result.Error.Messages[0].Code);
        tokens.DidNotReceive().Invalidate(Arg.Any<long>());
    }

    [Fact]
    public async Task GetPullRequestAsync_InvalidInstallationId_ReturnsValidationError()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsPullRequest, Error> result = await sut.GetPullRequestAsync("not-a-number", REPO, 42);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.installation_id.invalid", result.Error.Messages[0].Code);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task GetPullRequestDiffAsync_ParsesHunks()
    {
        const string prJson = """
            {
              "number": 1, "title": "t", "state": "open", "html_url": "u",
              "user": { "login": "x" },
              "head": { "ref": "h", "sha": "head_sha" },
              "base": { "ref": "main", "sha": "b" }
            }
            """;
        const string filesJson = """
            [
              {
                "filename": "src/foo.cs",
                "previous_filename": null,
                "status": "modified",
                "additions": 2,
                "deletions": 1,
                "patch": "@@ -10,3 +10,4 @@\n line1\n+added\n line3\n line4",
                "sha": "blob_sha"
              }
            ]
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, prJson),
            (HttpStatusCode.OK, filesJson));
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsDiff, Error> result = await sut.GetPullRequestDiffAsync(INSTALLATION_ID, REPO, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("head_sha", result.Value.HeadSha);
        Assert.Single(result.Value.Files);
        VcsDiffFile file = result.Value.Files[0];
        Assert.Equal("src/foo.cs", file.Path);
        Assert.Equal(2, file.Additions);
        Assert.Single(file.Hunks);
        VcsHunk hunk = file.Hunks[0];
        Assert.Equal(10, hunk.OldStart);
        Assert.Equal(3, hunk.OldLines);
        Assert.Equal(10, hunk.NewStart);
        Assert.Equal(4, hunk.NewLines);
    }

    [Fact]
    public async Task CompareAsync_ParsesFilesAndHunks()
    {
        const string compareJson = """
            {
              "status": "ahead",
              "files": [
                {
                  "filename": "src/foo.cs",
                  "previous_filename": null,
                  "status": "modified",
                  "additions": 3,
                  "deletions": 0,
                  "patch": "@@ -10,2 +10,5 @@\n line1\n+a\n+b\n+c",
                  "sha": "blob_sha"
                }
              ]
            }
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, compareJson);
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsDiff, Error> result = await sut.CompareAsync(INSTALLATION_ID, REPO, "base_sha", "head_sha");

        Assert.True(result.IsSuccess);
        Assert.Equal("head_sha", result.Value.HeadSha);
        Assert.Single(result.Value.Files);
        Assert.Equal(3, result.Value.TotalAdditions);
        Assert.Single(result.Value.Files[0].Hunks);
        // Compare endpoint URL form: {base}...{head}.
        Assert.Contains("compare/base_sha...head_sha", stub.Requests[0].RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompareAsync_NoFiles_ReturnsEmptyDiff()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(
            HttpStatusCode.OK, """{ "status": "identical", "files": [] }""");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsDiff, Error> result = await sut.CompareAsync(INSTALLATION_ID, REPO, "a", "a");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Files);
        Assert.Equal(0, result.Value.TotalAdditions);
    }

    [Fact]
    public async Task PostReviewAsync_PostsCorrectBodyAndAppendsSuggestion()
    {
        const string responseBody = """
            { "id": 9001, "html_url": "https://github.com/owner/repo/pull/1#pullrequestreview-9001" }
            """;

        // HttpRequestMessage.Content is disposed когда provider закрывает `using HttpRequestMessage`,
        // поэтому читаем body синхронно в stub-responder'е до возврата response'а.
        string? capturedBody = null;
        StubHttpMessageHandler stub = new(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            HttpResponseMessage resp = new(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            return resp;
        });
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        VcsReviewRequest request = new(
            "head_sha",
            "Overall looks good!",
            new[]
            {
                new VcsReviewComment("src/foo.cs", 12, "Maybe rename this?"),
                new VcsReviewComment("src/bar.cs", 5, "Suggested fix:", Suggestion: "var fixed = 1;"),
            });

        Result<VcsPostedReview, Error> result = await sut.PostReviewAsync(INSTALLATION_ID, REPO, 1, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(9001, result.Value.GitHubReviewId);
        Assert.NotNull(capturedBody);

        GitHubReviewRequestDto sent = JsonSerializer.Deserialize<GitHubReviewRequestDto>(capturedBody!)!;
        Assert.Equal("COMMENT", sent.Event);
        Assert.Equal("head_sha", sent.CommitId);
        Assert.Equal(2, sent.Comments.Count);
        Assert.All(sent.Comments, c => Assert.Equal("RIGHT", c.Side));
        Assert.Contains("```suggestion", sent.Comments[1].Body, StringComparison.Ordinal);
        Assert.Contains("var fixed = 1;", sent.Comments[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostReviewAsync_HttpTimeout_ReturnsVcsUnavailable()
    {
        StubHttpMessageHandler stub = new(_ => throw new TaskCanceledException("request timed out"));
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        VcsReviewRequest request = new("head_sha", "Summary", []);

        Result<VcsPostedReview, Error> result = await sut.PostReviewAsync(INSTALLATION_ID, REPO, 1, request);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.unavailable", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task GetRepoTreeAsync_ReturnsEntries()
    {
        const string body = """
            {
              "tree": [
                { "path": "src/foo.cs", "type": "blob", "size": 1234, "sha": "abc" },
                { "path": "src", "type": "tree", "size": null, "sha": "def" }
              ],
              "truncated": false
            }
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, body);
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<IReadOnlyList<VcsRepoTreeEntry>, Error> result =
            await sut.GetRepoTreeAsync(INSTALLATION_ID, REPO, "main");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("src/foo.cs", result.Value[0].Path);
        Assert.Equal("blob", result.Value[0].Type);
        Assert.Equal(1234, result.Value[0].Size);
    }

    [Fact]
    public async Task GetRepoTreeAsync_BranchWithSlash_UrlEncodedCorrectly()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{ "tree": [], "truncated": false }""");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        await sut.GetRepoTreeAsync(INSTALLATION_ID, REPO, "feature/foo");

        Assert.Single(stub.Requests);
        Assert.Contains("feature%2Ffoo", stub.Requests[0].RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileContentAsync_PathWithSpaces_UrlEncodedPerSegment()
    {
        const string body = """
            { "path": "docs/My File.md", "sha": "abc", "size": 100,
              "encoding": "base64", "content": "SGk=" }
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, body);
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        await sut.GetFileContentAsync(INSTALLATION_ID, REPO, "docs/My File.md");

        Assert.Single(stub.Requests);
        // Check the original (pre-normalization) string from the request — Uri may
        // decode %20→space внутреннего OriginalString иногда сохраняет escaped form.
        string requestUrl = stub.Requests[0].RequestUri!.OriginalString;
        Assert.Contains("docs/My%20File.md", requestUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("docs%2FMy", requestUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileContentAsync_HappyPath_ReturnsBase64Content()
    {
        const string body = """
            {
              "path": "src/foo.cs",
              "sha": "abc",
              "size": 100,
              "encoding": "base64",
              "content": "SGVsbG8gV29ybGQ="
            }
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, body);
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsFileContent, Error> result =
            await sut.GetFileContentAsync(INSTALLATION_ID, REPO, "src/foo.cs", "main");

        Assert.True(result.IsSuccess);
        Assert.Equal("base64", result.Value.Encoding);
        Assert.Equal("SGVsbG8gV29ybGQ=", result.Value.Content);
    }

    [Fact]
    public async Task GetInstallationDetailAsync_HappyPath_ReturnsParsedDetail()
    {
        const string detailJson = """
            {
              "id": 12345,
              "account": { "login": "student-1", "id": 9999, "type": "User" },
              "repository_selection": "all"
            }
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, detailJson);
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsInstallationDetail, Error> result = await sut.GetInstallationDetailAsync(12345L);

        Assert.True(result.IsSuccess);
        Assert.Equal("student-1", result.Value.OwnerLogin);
        Assert.Equal("9999", result.Value.OwnerExternalId);
        Assert.Equal(AssignmentReviewService.Domain.Vcs.VcsInstallationOwnerType.USER, result.Value.OwnerType);
        Assert.True(result.Value.RepoSelections.All);
        Assert.Empty(result.Value.RepoSelections.Repos);
    }

    [Fact]
    public async Task GetInstallationDetailAsync_OrgWithSelectedRepos_FetchesRepoList()
    {
        const string detailJson = """
            {
              "id": 12345,
              "account": { "login": "acme-corp", "id": 8888, "type": "Organization" },
              "repository_selection": "selected"
            }
            """;
        const string reposJson = """
            {
              "total_count": 2,
              "repositories": [
                { "full_name": "acme-corp/repo1" },
                { "full_name": "acme-corp/repo2" }
              ]
            }
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, detailJson),
            (HttpStatusCode.OK, reposJson));
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsInstallationDetail, Error> result = await sut.GetInstallationDetailAsync(12345L);

        Assert.True(result.IsSuccess);
        Assert.Equal(AssignmentReviewService.Domain.Vcs.VcsInstallationOwnerType.ORG, result.Value.OwnerType);
        Assert.False(result.Value.RepoSelections.All);
        Assert.Equal(2, result.Value.RepoSelections.Repos.Count);
        Assert.Contains("acme-corp/repo1", result.Value.RepoSelections.Repos);
    }

    [Fact]
    public async Task GetInstallationDetailAsync_HttpTimeout_ReturnsVcsUnavailable()
    {
        StubHttpMessageHandler stub = new(_ => throw new TaskCanceledException("request timed out"));
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsInstallationDetail, Error> result = await sut.GetInstallationDetailAsync(12345L);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.unavailable", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task GetInstallationDetailAsync_404_ReturnsNotFound()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsInstallationDetail, Error> result = await sut.GetInstallationDetailAsync(12345L);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.resource.not_found", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task GetInstallationDetailAsync_TokenFailure_PropagatesError()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        (GitHubVcsProvider sut, _, _) = BuildSut(
            stub,
            tokenOverride: VcsErrors.InstallationTokenFailed("not configured"));

        Result<VcsInstallationDetail, Error> result = await sut.GetInstallationDetailAsync(12345L);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.installation_token.failed", result.Error.Messages[0].Code);
        // GET /app/installations/{id} runs first under App JWT (not installation token);
        // installation token is needed only afterwards for /installation/repositories.
        // So one request hits the wire before the installation-token fetch fails.
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task GetPullRequestAsync_TokenFailure_PropagatesError()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        (GitHubVcsProvider sut, _, _) = BuildSut(
            stub,
            tokenOverride: VcsErrors.InstallationTokenFailed("not configured"));

        Result<VcsPullRequest, Error> result = await sut.GetPullRequestAsync(INSTALLATION_ID, REPO, 42);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.installation_token.failed", result.Error.Messages[0].Code);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task GetReviewCommentsAsync_HappyPath_ReturnsBodiesAndHitsReviewCommentsEndpoint()
    {
        // #383: тела inline-комментов прошлого review'а тянутся по review_id для
        // re-review prior-context. line nullable (outdated) → fallback на original_line.
        const string body = """
            [
              { "path": "src/Foo.cs", "line": 12, "original_line": 12, "body": "Инициализируй Created_At." },
              { "path": "src/Bar.cs", "line": null, "original_line": 7, "body": "Добавь валидацию входа." }
            ]
            """;
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, body);
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<IReadOnlyList<VcsReviewComment>, Error> result =
            await sut.GetReviewCommentsAsync(INSTALLATION_ID, REPO, 42, reviewId: 555);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("src/Foo.cs", result.Value[0].Path);
        Assert.Equal(12, result.Value[0].Line);
        Assert.Equal("Инициализируй Created_At.", result.Value[0].Body);
        // line=null → используется original_line.
        Assert.Equal(7, result.Value[1].Line);

        Assert.Single(stub.Requests);
        Assert.Contains(
            "pulls/42/reviews/555/comments",
            stub.Requests[0].RequestUri!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReviewCommentsAsync_EmptyArray_ReturnsEmptyList()
    {
        // Review без inline-комментов (summary-only) → пустой список, не ошибка.
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<IReadOnlyList<VcsReviewComment>, Error> result =
            await sut.GetReviewCommentsAsync(INSTALLATION_ID, REPO, 42, reviewId: 555);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetReviewCommentsAsync_404_ReturnsNotFoundError()
    {
        StubHttpMessageHandler stub = StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<IReadOnlyList<VcsReviewComment>, Error> result =
            await sut.GetReviewCommentsAsync(INSTALLATION_ID, REPO, 42, reviewId: 555);

        Assert.True(result.IsFailure);
        Assert.Equal("vcs.resource.not_found", result.Error.Messages[0].Code);
    }

    private const string PR_JSON = """
        {
          "number": 1, "title": "t", "state": "open", "html_url": "u",
          "user": { "login": "x" },
          "head": { "ref": "h", "sha": "head_sha" },
          "base": { "ref": "main", "sha": "b" }
        }
        """;

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task GetPullRequestDiffAsync_PaginatesBeyond100Files()
    {
        // owlillp/DirectoryService#38: PR на 272 файла. GitHub режет files endpoint на
        // страницы по 100; без пагинации хвост (frontend/ сортируется ПОСЛЕ backend/)
        // выпадал → модель видела «только backend» и ложно объявляла frontend отсутствующим.
        // Тянем все страницы, пока GitHub не отдаст неполную.
        string page1 = "[" + string.Join(",", Enumerable.Range(1, 100).Select(i =>
            $$"""{"filename":"backend/F{{i}}.cs","previous_filename":"old/F{{i}}.cs","status":"renamed","additions":0,"deletions":0,"patch":null,"sha":"s{{i}}"}""")) + "]";
        const string page2 = """
            [{"filename":"frontend/src/app/page.tsx","previous_filename":null,"status":"added","additions":12,"deletions":0,"patch":"@@ -0,0 +1,12 @@\n+code","sha":"fe"}]
            """;

        StubHttpMessageHandler stub = new(req =>
        {
            string url = req.RequestUri!.ToString();
            if (!url.Contains("/files", StringComparison.Ordinal))
                return Ok(PR_JSON);
            // Прим.: "per_page=100" содержит подстроку "page=1" — матчим именно "page=2".
            return Ok(url.Contains("page=2", StringComparison.Ordinal) ? page2 : page1);
        });
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsDiff, Error> result = await sut.GetPullRequestDiffAsync(INSTALLATION_ID, REPO, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(101, result.Value.Files.Count);
        Assert.Contains(result.Value.Files, f => string.Equals(f.Path, "frontend/src/app/page.tsx", StringComparison.Ordinal));
        // Вторая страница реально запрошена.
        Assert.Contains(stub.Requests, r => r.RequestUri!.ToString().Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPullRequestDiffAsync_SinglePage_DoesNotRequestSecondPage()
    {
        // ≤100 файлов → неполная первая страница → второй запрос не делаем.
        const string filesJson = """
            [
              {"filename":"src/A.cs","previous_filename":null,"status":"modified","additions":3,"deletions":1,"patch":"@@ -1 +1,3 @@\n+a","sha":"a"},
              {"filename":"src/B.cs","previous_filename":null,"status":"added","additions":5,"deletions":0,"patch":"@@ -0,0 +1,5 @@\n+b","sha":"b"}
            ]
            """;
        StubHttpMessageHandler stub = new(req =>
        {
            string url = req.RequestUri!.ToString();
            return Ok(url.Contains("/files", StringComparison.Ordinal) ? filesJson : PR_JSON);
        });
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsDiff, Error> result = await sut.GetPullRequestDiffAsync(INSTALLATION_ID, REPO, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Files.Count);
        Assert.DoesNotContain(stub.Requests, r => r.RequestUri!.ToString().Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPullRequestDiffAsync_ExactlyOnePageOfFiles_StillFetchesSecondPage()
    {
        // Граница: ровно 100 файлов на page=1 (count == FILES_PER_PAGE, не < ) → цикл НЕ
        // прерывается, запрашивает page=2, получает пустой массив → 100 файлов итого.
        // Пиннит оператор `< FILES_PER_PAGE` (а не `<=`).
        string page1 = "[" + string.Join(",", Enumerable.Range(1, 100).Select(i =>
            $$"""{"filename":"src/F{{i}}.cs","previous_filename":null,"status":"modified","additions":1,"deletions":0,"patch":"@@ -1 +1 @@\n+a","sha":"s{{i}}"}""")) + "]";

        StubHttpMessageHandler stub = new(req =>
        {
            string url = req.RequestUri!.ToString();
            if (!url.Contains("/files", StringComparison.Ordinal))
                return Ok(PR_JSON);
            return Ok(url.Contains("page=2", StringComparison.Ordinal) ? "[]" : page1);
        });
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsDiff, Error> result = await sut.GetPullRequestDiffAsync(INSTALLATION_ID, REPO, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value.Files.Count);
        Assert.Contains(stub.Requests, r => r.RequestUri!.ToString().Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompareAsync_PaginatesBeyond100Files()
    {
        // Within-review re-review может нести >100 файлов — compare endpoint режется так же.
        string page1Files = string.Join(",", Enumerable.Range(1, 100).Select(i =>
            $$"""{"filename":"src/F{{i}}.cs","previous_filename":null,"status":"modified","additions":2,"deletions":0,"patch":"@@ -1 +1,2 @@\n+a","sha":"s{{i}}"}"""));
        string page1 = $$"""{ "status": "ahead", "files": [{{page1Files}}] }""";
        const string page2 = """
            { "status": "ahead", "files": [{"filename":"src/Last.cs","previous_filename":null,"status":"added","additions":3,"deletions":0,"patch":"@@ -0,0 +1,3 @@\n+x","sha":"last"}] }
            """;

        // Прим.: "per_page=100" содержит подстроку "page=1" — матчим именно "page=2".
        StubHttpMessageHandler stub = new(req =>
            Ok(req.RequestUri!.ToString().Contains("page=2", StringComparison.Ordinal) ? page2 : page1));
        (GitHubVcsProvider sut, _, _) = BuildSut(stub);

        Result<VcsDiff, Error> result = await sut.CompareAsync(INSTALLATION_ID, REPO, "base_sha", "head_sha");

        Assert.True(result.IsSuccess);
        Assert.Equal(101, result.Value.Files.Count);
        Assert.Contains(result.Value.Files, f => string.Equals(f.Path, "src/Last.cs", StringComparison.Ordinal));
        Assert.Contains(stub.Requests, r => r.RequestUri!.ToString().Contains("page=2", StringComparison.Ordinal));
    }
}
