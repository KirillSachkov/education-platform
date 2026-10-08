using NotificationService.Contracts.Inbox.Requests;
using NotificationService.Core.Features.Inbox.UseCases;

namespace NotificationService.UnitTests.Inbox;

public sealed class GetMyNotificationsValidatorTests
{
    private readonly GetMyNotificationsValidator _sut = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Partial_cursor_should_be_rejected(bool timestampOnly)
    {
        var request = new GetNotificationsRequest
        {
            CursorBefore = timestampOnly ? DateTime.UtcNow : null,
            CursorId = timestampOnly ? null : Guid.CreateVersion7(),
        };

        var result = await _sut.ValidateAsync(new GetMyNotificationsQuery(request));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Too_many_types_should_be_rejected()
    {
        var request = new GetNotificationsRequest
        {
            Types = Enumerable.Range(1, 33).Select(value => (short)value).ToArray(),
        };

        var result = await _sut.ValidateAsync(new GetMyNotificationsQuery(request));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Unknown_type_should_be_rejected()
    {
        var request = new GetNotificationsRequest { Types = [short.MaxValue] };

        var result = await _sut.ValidateAsync(new GetMyNotificationsQuery(request));

        Assert.False(result.IsValid);
    }
}
