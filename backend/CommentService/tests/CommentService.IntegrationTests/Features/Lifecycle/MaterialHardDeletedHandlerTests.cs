using CommentService.Domain;
using CommentService.IntegrationTests.Infrastructure;
using Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Wolverine;

namespace CommentService.IntegrationTests.Features.Lifecycle;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class MaterialHardDeletedHandlerTests : CommentServiceTestsBase
{
    public MaterialHardDeletedHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Handle_DeletesAllCommentsForMaterial_IncludingSoftDeletedAndChildren()
    {
        Guid materialId = Guid.NewGuid();
        Guid otherMaterialId = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            // Каждому комментарию свой instance EntityReference — OwnsOne не любит шаринг
            // одного value object'а между несколькими entity'ями (трекер ругается на null'ы).
            Comment root = Comment.CreateParent(
                MockMainAuthorId,
                CommentEntityReference.Of(EntityType.Material, materialId).Value,
                Content.Of("root").Value);
            await db.Comments.AddAsync(root);

            Comment child = Comment.CreateChild(
                root,
                MockSecondAuthorId,
                Content.Of("child").Value);
            await db.Comments.AddAsync(child);

            Comment softDeleted = Comment.CreateParent(
                MockMainAuthorId,
                CommentEntityReference.Of(EntityType.Material, materialId).Value,
                Content.Of("ghost").Value);
            softDeleted.SoftDelete();
            await db.Comments.AddAsync(softDeleted);

            // Комментарий на другом материале — не должен пострадать.
            Comment unrelated = Comment.CreateParent(
                MockMainAuthorId,
                CommentEntityReference.Of(EntityType.Material, otherMaterialId).Value,
                Content.Of("survives").Value);
            await db.Comments.AddAsync(unrelated);

            await db.SaveChangesAsync();
        });

        IMessageBus bus = Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new MaterialHardDeleted(materialId));

        await ExecuteInDb(async db =>
        {
            int remainingForDeleted = await db.Comments
                .IgnoreQueryFilters()
                .CountAsync(c => c.EntityReference.Type == EntityType.Material
                              && c.EntityReference.Id == materialId);
            Assert.Equal(0, remainingForDeleted);

            int remainingForOther = await db.Comments
                .IgnoreQueryFilters()
                .CountAsync(c => c.EntityReference.Type == EntityType.Material
                              && c.EntityReference.Id == otherMaterialId);
            Assert.Equal(1, remainingForOther);
        });
    }

    [Fact]
    public async Task Handle_WithNoMatchingComments_IsNoOp()
    {
        Guid neverUsedMaterialId = Guid.NewGuid();
        Guid existingMaterialId = Guid.NewGuid();

        // Sanity-фон: handler не должен трогать комментарии другого материала.
        await ExecuteInDb(async db =>
        {
            Comment comment = Comment.CreateParent(
                MockMainAuthorId,
                CommentEntityReference.Of(EntityType.Material, existingMaterialId).Value,
                Content.Of("untouched").Value);
            await db.Comments.AddAsync(comment);
            await db.SaveChangesAsync();
        });

        IMessageBus bus = Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new MaterialHardDeleted(neverUsedMaterialId));

        await ExecuteInDb(async db =>
        {
            int forNeverUsed = await db.Comments
                .IgnoreQueryFilters()
                .CountAsync(c => c.EntityReference.Type == EntityType.Material
                              && c.EntityReference.Id == neverUsedMaterialId);
            Assert.Equal(0, forNeverUsed);

            int forExisting = await db.Comments
                .IgnoreQueryFilters()
                .CountAsync(c => c.EntityReference.Type == EntityType.Material
                              && c.EntityReference.Id == existingMaterialId);
            Assert.Equal(1, forExisting);
        });
    }

    [Theory]
    [InlineData(EntityType.Course)]
    [InlineData(EntityType.Issue)]
    [InlineData(EntityType.Quiz)]
    public async Task Handle_DeletesCommentsForEverySupportedHardDeletedTarget(EntityType entityType)
    {
        Guid targetId = Guid.CreateVersion7();

        await ExecuteInDb(async db =>
        {
            Comment comment = Comment.CreateParent(
                MockMainAuthorId,
                CommentEntityReference.Of(entityType, targetId).Value,
                Content.Of("deleted target").Value);
            await db.Comments.AddAsync(comment);
            await db.SaveChangesAsync();
        });

        IMessageBus bus = Services.GetRequiredService<IMessageBus>();
        switch (entityType)
        {
            case EntityType.Course:
                await bus.InvokeAsync(new CourseHardDeleted(targetId));
                break;
            case EntityType.Issue:
                await bus.InvokeAsync(new IssueHardDeleted(targetId));
                break;
            case EntityType.Quiz:
                await bus.InvokeAsync(new QuizHardDeleted(targetId));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entityType), entityType, null);
        }

        int remaining = await ExecuteInDb(db => db.Comments
            .IgnoreQueryFilters()
            .CountAsync(c => c.EntityReference.Type == entityType
                          && c.EntityReference.Id == targetId));

        Assert.Equal(0, remaining);
    }
}
