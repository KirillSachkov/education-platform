using CSharpFunctionalExtensions;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Unit;

/// <summary>
///     Unit-тесты для агрегата <see cref="Material" /> — чистый домен,
///     не требует DB/DI/Testcontainers. Покрывает конструкцию, state machine
///     публикации и инвариант «нельзя опубликовать без контента или видео».
/// </summary>
public class MaterialTests
{
    private const int BoundCourseCount = 1;

    private static Title NewTitle(string value = "Пример материала")
        => Title.Create(value).Value;

    private static MarkdownContent NewContent(string value = "Тело материала в Markdown")
        => MarkdownContent.Create(value).Value;

    private static VideoId NewVideoId()
        => VideoId.Create(Guid.NewGuid()).Value;

    private static ImageId NewImageId()
        => ImageId.Create(Guid.NewGuid()).Value;

    private static Material NewMaterial(
        MaterialKind kind = MaterialKind.ARTICLE,
        AccessType accessType = AccessType.PUBLIC)
        => new(Guid.NewGuid(), NewTitle(), kind, accessType);

    [Fact]
    public void Create_NewMaterial_ShouldBeInDraftStatus()
    {
        Material material = NewMaterial();

        Assert.Equal(PublicationStatus.DRAFT, material.Status);
    }

    [Fact]
    public void Create_NewMaterial_ShouldGenerateNonEmptyId()
    {
        Material material = NewMaterial();

        Assert.NotEqual(Guid.Empty, material.Id);
    }

    [Fact]
    public void Create_NewMaterial_DefaultKind_IsArticle()
    {
        // Проверяем дефолт, не передавая kind явно.
        Material material = new(Guid.NewGuid(), NewTitle());

        Assert.Equal(MaterialKind.ARTICLE, material.Kind);
    }

    [Fact]
    public void Create_NewMaterial_DefaultAccessType_IsPublic()
    {
        Material material = new(Guid.NewGuid(), NewTitle());

        Assert.Equal(AccessType.PUBLIC, material.AccessType);
    }

    [Fact]
    public void Create_NewMaterial_ContentAndMediaAreNull()
    {
        Material material = NewMaterial();

        Assert.Null(material.Content);
        Assert.Null(material.VideoId);
        Assert.Null(material.ImageId);
    }

    [Fact]
    public void Publish_FromDraft_WithContent_Succeeds()
    {
        Material material = NewMaterial();
        material.Update(NewTitle(), NewContent(), MaterialKind.ARTICLE, AccessType.ENROLLED, BoundCourseCount, description: null);

        UnitResult<Error> result = material.Publish();

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.PUBLISHED, material.Status);
    }

    [Fact]
    public void Publish_FromDraft_WithVideoOnly_Succeeds()
    {
        Material material = NewMaterial(MaterialKind.VIDEO);
        material.AttachVideo(NewVideoId());

        UnitResult<Error> result = material.Publish();

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.PUBLISHED, material.Status);
    }

    [Fact]
    public void Publish_FromDraft_WithNeitherContentNorVideo_Fails_WithSpecificErrorCode()
    {
        Material material = NewMaterial();

        UnitResult<Error> result = material.Publish();

        Assert.True(result.IsFailure);
        Assert.Equal("material.publish.content_or_video_required", result.Error.Messages[0].Code);
        Assert.Equal(PublicationStatus.DRAFT, material.Status);
    }

    [Fact]
    public void ValidateProspectivePayload_PublishedWithoutContentOrVideo_Fails()
    {
        Material material = NewMaterial(MaterialKind.VIDEO);
        material.AttachVideo(NewVideoId());
        Assert.True(material.Publish().IsSuccess);

        UnitResult<Error> result = material.ValidateProspectivePayload(content: null, videoId: null);

        Assert.True(result.IsFailure);
        Assert.Equal("material.publish.content_or_video_required", result.Error.Messages[0].Code);
        Assert.Equal(PublicationStatus.PUBLISHED, material.Status);
        Assert.NotNull(material.VideoId);
    }

    [Fact]
    public void Publish_FromArchived_Succeeds()
    {
        // Article-style гибкий state machine: Archived → Published разрешён.
        Material material = NewMaterial();
        material.Update(NewTitle(), NewContent(), MaterialKind.ARTICLE, AccessType.ENROLLED, BoundCourseCount, description: null);
        Assert.True(material.Publish().IsSuccess);
        Assert.True(material.Archive().IsSuccess);

        UnitResult<Error> result = material.Publish();

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.PUBLISHED, material.Status);
    }

    [Fact]
    public void SendToDraft_FromPublished_Succeeds()
    {
        Material material = NewMaterial();
        material.Update(NewTitle(), NewContent(), MaterialKind.ARTICLE, AccessType.ENROLLED, BoundCourseCount, description: null);
        Assert.True(material.Publish().IsSuccess);

        UnitResult<Error> result = material.SendToDraft();

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.DRAFT, material.Status);
    }

    [Fact]
    public void SendToDraft_FromDraft_Fails()
    {
        Material material = NewMaterial();

        UnitResult<Error> result = material.SendToDraft();

        Assert.True(result.IsFailure);
        Assert.Equal("material.invalid.status.transition", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Archive_FromPublished_Succeeds()
    {
        Material material = NewMaterial();
        material.Update(NewTitle(), NewContent(), MaterialKind.ARTICLE, AccessType.ENROLLED, BoundCourseCount, description: null);
        Assert.True(material.Publish().IsSuccess);

        UnitResult<Error> result = material.Archive();

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.ARCHIVED, material.Status);
    }

    [Fact]
    public void Archive_FromDraft_Fails()
    {
        Material material = NewMaterial();

        UnitResult<Error> result = material.Archive();

        Assert.True(result.IsFailure);
        Assert.Equal("material.invalid.status.transition", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Update_ChangesFields_And_BumpsUpdatedAt()
    {
        Material material = NewMaterial();
        DateTime before = material.UpdatedAt;

        // Гарантируем различимую DateTime.UtcNow — без Thread.Sleep (запрещён анализатором).
        await Task.Delay(10);

        Title newTitle = Title.Create("Новое название").Value;
        MarkdownContent newContent = MarkdownContent.Create("Обновлённое тело").Value;

        UnitResult<Error> result = material.Update(
            newTitle,
            newContent,
            MaterialKind.NOTE,
            AccessType.REGISTERED,
            BoundCourseCount,
            description: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(newTitle, material.Title);
        Assert.Equal(newContent, material.Content);
        Assert.Equal(MaterialKind.NOTE, material.Kind);
        Assert.Equal(AccessType.REGISTERED, material.AccessType);
        Assert.True(material.UpdatedAt > before);
    }

    [Fact]
    public void Update_ToCourseScopedAccessWithoutBoundCourse_Succeeds_AfterPlanBoundRefactor()
    {
        // После #77: orphan ENROLLED легитимен (гейт через платформенный plan:all),
        // INV-3 снят — Update больше не отклоняет такие комбинации.
        Material material = NewMaterial();

        UnitResult<Error> result = material.Update(
            NewTitle(),
            NewContent(),
            MaterialKind.ARTICLE,
            AccessType.ENROLLED,
            boundCourseCount: 0,
            description: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccessType.ENROLLED, material.AccessType);
    }

    [Fact]
    public void AttachVideo_SetsVideoId()
    {
        Material material = NewMaterial();
        VideoId videoId = NewVideoId();

        material.AttachVideo(videoId);

        Assert.Equal(videoId, material.VideoId);
    }

    [Fact]
    public void DetachVideo_ClearsVideoId_ToNull()
    {
        Material material = NewMaterial();
        material.AttachVideo(NewVideoId());

        material.DetachVideo();

        Assert.Null(material.VideoId);
    }

    [Fact]
    public void AttachImage_SetsImageId()
    {
        Material material = NewMaterial();
        ImageId imageId = NewImageId();

        material.AttachImage(imageId);

        Assert.Equal(imageId, material.ImageId);
    }

    [Fact]
    public void DetachImage_ClearsImageId_ToNull()
    {
        Material material = NewMaterial();
        material.AttachImage(NewImageId());

        material.DetachImage();

        Assert.Null(material.ImageId);
    }
}
