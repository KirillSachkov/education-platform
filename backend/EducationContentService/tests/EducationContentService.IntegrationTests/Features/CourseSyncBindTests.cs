using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CourseSyncBindTests : EducationContentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public CourseSyncBindTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateCourse_AttachesPreview_BindsAndAttachesImage()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid previewAssetId = Guid.CreateVersion7();

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        var request = new UpdateCourseRequest(
            "New Title",
            "New Desc",
            null,
            PreviewId: previewAssetId);

        HttpResponseMessage response = await PatchAsJsonAsync($"/courses/{courseId}", request);
        response.EnsureSuccessStatusCode();

        await fileClient.Received(1).BindAssetInternalAsync(
            previewAssetId,
            Arg.Is<BindAssetInternalRequest>(r =>
                r.TargetEntity.Type == "course" && r.TargetEntity.Id == courseId),
            Arg.Any<CancellationToken>());

        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.SingleAsync(c => c.Id == courseId, ct);
            Assert.NotNull(course.ImageId);
            Assert.Equal(previewAssetId, course.ImageId!.Value);
        });
    }

    [Fact]
    public async Task UpdateCourse_DetachesPreview_WhenPreviewIdNullAndPreviousExists()
    {
        CancellationToken ct = CancellationToken.None;
        Guid existingPreview = Guid.CreateVersion7();
        Guid courseId = await CreateCourseInDb(ct, withImageId: existingPreview);

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        var request = new UpdateCourseRequest(
            "No Preview",
            "Plain",
            null,
            PreviewId: null);

        HttpResponseMessage response = await PatchAsJsonAsync($"/courses/{courseId}", request);
        response.EnsureSuccessStatusCode();

        FileAssetDetached detached = Assert.Single(_factory.OutboxCollector.OfType<FileAssetDetached>());
        Assert.Equal(existingPreview, detached.AssetId);
        Assert.Equal(0, detached.ExpectedBindingRevision);
        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.SingleAsync(c => c.Id == courseId, ct);
            Assert.Null(course.ImageId);
        });
    }

    private async Task<Guid> CreateCourseInDb(CancellationToken ct, Guid? withImageId = null)
    {
        Guid courseId = Guid.Empty;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        await ExecuteInDb(async db =>
        {
            string slug = $"test-{Guid.NewGuid():N}"[..16];
            Course course = new(
                authorId,
                Title.Create($"Course {Guid.NewGuid():N}").Value,
                Description.Create("Sync bind course").Value,
                CourseSlug.Create(slug).Value,
                Ordering.SortKey.Initial());

            if (withImageId is { } imageId)
            {
                course.AttachImage(ImageId.Create(imageId).Value);
            }

            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            courseId = course.Id;
        });

        return courseId;
    }
}
