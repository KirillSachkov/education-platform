using ContentAccess;
using EducationContentService.Core.Features.ContentAccess;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.IntegrationTests.Unit.ContentAccess;

public sealed class CollectionAccessSyncTests
{
    [Fact]
    public async Task CollectionCreated_UnknownAccessType_ThrowsWithoutWritingPublicTags()
    {
        IResourceAccessWriter writer = Substitute.For<IResourceAccessWriter>();
        var message = new CollectionCreated(Guid.NewGuid(), "FUTURE_POLICY", null, Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SyncCollectionAccessOnCreationHandler.HandleAsync(
                message,
                writer,
                NullLogger.Instance));

        await writer.DidNotReceive().SetTagsAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }
}
