using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Unit.HttpCommunication;

public sealed class EducationContentServiceClientTests
{
    [Theory]
    [InlineData(200, 1)]
    [InlineData(201, 2)]
    public async Task GetCourseProgressBlueprintsAsync_ChunksRequestsByTwoHundred(
        int count,
        int expectedCalls)
    {
        Guid[] ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var handler = new BlueprintHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://ecs.test") };
        var sut = new EducationContentServiceClient(
            httpClient,
            NullLogger<EducationContentServiceClient>.Instance);

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> result =
            await sut.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest(ids),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ids, result.Value.Select(x => x.CourseId));
        Assert.Equal(expectedCalls, handler.BatchSizes.Count);
        Assert.All(handler.BatchSizes, size => Assert.InRange(size, 1, 200));
    }

    private sealed class BlueprintHandler : HttpMessageHandler
    {
        public List<int> BatchSizes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            GetCourseProgressBlueprintsRequest body =
                (await request.Content!.ReadFromJsonAsync<GetCourseProgressBlueprintsRequest>(cancellationToken))!;
            BatchSizes.Add(body.CourseIds.Count);

            IReadOnlyCollection<CourseProgressBlueprintDto> result = body.CourseIds
                .Select(id => new CourseProgressBlueprintDto(
                    id, id.ToString("N"), "Course", "Description", null, null,
                    0, 0, [], 0, 0, [], 0, false, "a0", "COURSE"))
                .ToArray();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Envelope<IReadOnlyCollection<CourseProgressBlueprintDto>>.Ok(result)),
            };
        }
    }
}