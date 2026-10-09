using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RetainedMaterialArtifactsTests(IntegrationTestsWebFactory factory)
    : EducationContentServiceTestsBase(factory)
{
    [Fact]
    public async Task PublishedVideoDetail_ReturnsStoredContentAndChaptersWithoutProcessingService()
    {
        Guid materialId = await ExecuteInDb(async db =>
        {
            var material = new Material(
                Guid.CreateVersion7(),
                Title.Create("Существующий видеоурок").Value,
                MaterialKind.VIDEO,
                AccessType.PUBLIC);
            material.SetContent(MarkdownContent.Create("# Сохранённый конспект\n\nСодержимое урока.").Value);
            material.AttachVideo(VideoId.Create(Guid.CreateVersion7()).Value);
            material.UpdateChapters(["Введение", "Практика"], [0, 120]);
            Assert.True(material.Publish().IsSuccess);
            db.Materials.Add(material);
            await db.SaveChangesAsync();
            return material.Id;
        });
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail/");

        response.EnsureSuccessStatusCode();
        MaterialDetailDto detail = await ReadResultAsync<MaterialDetailDto>(response);
        Assert.Equal("# Сохранённый конспект\n\nСодержимое урока.", detail.Content);
        Assert.Equal(
            [new MaterialChapterDto("Введение", 0), new MaterialChapterDto("Практика", 120)],
            detail.Chapters);
        Assert.True(detail.IsAccessible);
    }
}