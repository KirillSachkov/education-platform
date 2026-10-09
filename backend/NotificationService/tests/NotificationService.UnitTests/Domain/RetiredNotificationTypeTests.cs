using NotificationService.Domain.Notifications;

namespace NotificationService.UnitTests.Domain;

public class RetiredNotificationTypeTests
{
    [Theory]
    [InlineData(18, "UserLeveledUp")]
    [InlineData(19, "LevelTestInvite")]
    public void PersistedType_RemainsReadable_AndIsMarkedObsolete(int value, string name)
    {
        Assert.Equal(name, ((NotificationType)value).ToString());
        var field = typeof(NotificationType).GetField(name);
        Assert.NotNull(field);
        Assert.True(Attribute.IsDefined(field, typeof(ObsoleteAttribute)));
    }
}