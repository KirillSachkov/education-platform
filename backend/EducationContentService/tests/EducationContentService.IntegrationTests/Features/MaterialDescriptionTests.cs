using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.IntegrationTests.Infrastructure;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class MaterialDescriptionTests : EducationContentServiceTestsBase
{
    public MaterialDescriptionTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task MaterialDescription_CreateUpdateClear_RoundTripsThroughDetail()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        // Markdown с переводом строки — доказывает, что description НЕ схлопывается в whitespace
        // и хранится/отдаётся побайтово (отдельно от Content).
        const string initialDescription = "**Полезные ссылки**\n- https://example.com";

        // 1. Create (author) с Description.
        AuthenticateAs(authorId, "platform-author");

        var createRequest = new CreateMaterialRequest(
            Title: "Материал с описанием",
            Content: "# Содержимое",
            Kind: MaterialKind.ARTICLE.ToString(),
            AccessType: AccessType.PUBLIC.ToString(),
            Description: initialDescription);

        HttpResponseMessage createResponse =
            await AppHttpClient.PostAsJsonAsync("/materials", createRequest, ct);
        createResponse.EnsureSuccessStatusCode();

        Guid materialId = await ReadResultAsync<Guid>(createResponse);

        // 2. GET detail → Description равен отправленному, перевод строки/markdown целы.
        MaterialDetailDto created = await GetDetailAsync(authorId, materialId, ct);
        Assert.Equal(initialDescription, created.Description);
        // Content и Description — независимые поля.
        Assert.Equal("# Содержимое", created.Content);

        // 3. Update новым Description → GET detail → обновилось.
        const string updatedDescription = "## Обновлённое\n1. Первый\n2. Второй";

        var updateRequest = new UpdateMaterialRequest(
            Title: "Материал с описанием",
            Content: "# Содержимое",
            Kind: MaterialKind.ARTICLE.ToString(),
            AccessType: AccessType.PUBLIC.ToString(),
            Description: updatedDescription);

        HttpResponseMessage updateResponse =
            await AppHttpClient.PatchAsJsonAsync($"/materials/{materialId}", updateRequest, ct);
        updateResponse.EnsureSuccessStatusCode();

        MaterialDetailDto updated = await GetDetailAsync(authorId, materialId, ct);
        Assert.Equal(updatedDescription, updated.Description);

        // 4. Update с Description=null (PUT-семантика) → описание очищается.
        var clearRequest = new UpdateMaterialRequest(
            Title: "Материал с описанием",
            Content: "# Содержимое",
            Kind: MaterialKind.ARTICLE.ToString(),
            AccessType: AccessType.PUBLIC.ToString(),
            Description: null);

        HttpResponseMessage clearResponse =
            await AppHttpClient.PatchAsJsonAsync($"/materials/{materialId}", clearRequest, ct);
        clearResponse.EnsureSuccessStatusCode();

        MaterialDetailDto cleared = await GetDetailAsync(authorId, materialId, ct);
        Assert.Null(cleared.Description);
    }

    private async Task<MaterialDetailDto> GetDetailAsync(
        Guid authorId, Guid materialId, CancellationToken ct)
    {
        // PUBLIC-материал: автор + grant-all checker дают доступ к detail (mirrors MaterialQueriesTests).
        AuthenticateAs(authorId, "platform-author");
        EntitlementChecker.GrantAll();

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);
        response.EnsureSuccessStatusCode();

        return await ReadResultAsync<MaterialDetailDto>(response);
    }
}
