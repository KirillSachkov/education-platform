using CSharpFunctionalExtensions;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.MaterialProcessing.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>video.auto_processing.failed</c> (#648):
/// авто-обработка видео упала → владельцу видео приходит уведомление с дип-линком в
/// редактор материала. Идемпотентность по <c>JobId</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class VideoAutoProcessingFailedHandlerTests : NotificationServiceTestsBase
{
    public VideoAutoProcessingFailedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AutoProcessingFailed_NotifiesOwner_WithEditorDeepLink()
    {
        Guid ownerId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();

        StubMaterialLookup(materialId, "Урок про async/await");

        await InvokeMessageAndWaitAsync(new VideoAutoProcessingFailed(
            JobId: jobId,
            MaterialId: materialId,
            VideoAssetId: Guid.NewGuid(),
            OwnerUserId: ownerId,
            ErrorCode: "timecodes.transcription.failed",
            ErrorMessage: "Не удалось распознать аудиодорожку видео"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == ownerId));

        Assert.Equal(NotificationType.VideoAutoProcessingFailed, notification.Type);
        Assert.Equal(jobId, notification.CorrelationId);
        Assert.Contains("Урок про async/await", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Не удалось распознать аудиодорожку видео", notification.Body, StringComparison.Ordinal);
        // Дип-линк ведёт в редактор материала, где автор перезапускает обработку вручную.
        Assert.Contains($"/author/knowledge-base/edit/{materialId}", notification.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoProcessingFailed_Twice_IsIdempotent()
    {
        Guid ownerId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        StubMaterialLookup(materialId, "Материал");

        VideoAutoProcessingFailed evt = new(
            JobId: Guid.NewGuid(),
            MaterialId: materialId,
            VideoAssetId: Guid.NewGuid(),
            OwnerUserId: ownerId,
            ErrorCode: "timecodes.transcription.failed",
            ErrorMessage: "ошибка");

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == ownerId));
        Assert.Equal(1, count);
    }

    private void StubMaterialLookup(Guid materialId, string title)
    {
        EducationContentClient.GetMaterialSearchLookupAsync(materialId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<MaterialSearchLookupDto, Error>(new MaterialSearchLookupDto(
                Id: materialId,
                CourseId: null,
                CourseSlug: null,
                Title: title,
                ImageId: null,
                ModuleId: null,
                CourseTitle: null,
                ModuleTitle: null,
                Status: PublicationStatus.PUBLISHED,
                RequiredAccessTags: [],
                UpdatedAt: DateTime.UtcNow,
                Content: null)));
    }
}
