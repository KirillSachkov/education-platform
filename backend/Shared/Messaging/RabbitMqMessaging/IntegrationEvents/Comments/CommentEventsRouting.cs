namespace Shared.Messaging.IntegrationEvents.Comments;

public static class CommentEventsRouting
{
    public const string EXCHANGE = "comment.events";

    public static class RoutingKeys
    {
        public static string CommentCreated() => "comment.created";
    }
}
