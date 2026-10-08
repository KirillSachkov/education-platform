namespace Shared.Messaging.IntegrationEvents.Tags;

public static class TagEventsRouting
{
    public const string EXCHANGE = "tag.events";

    public static class RoutingKeys
    {
        public static string TagsAddedToEntity() => "tags.added_to_entity";
        public static string TagsRemovedFromEntity() => "tags.removed_from_entity";
        public static string TagsDeleted() => "tags.deleted";
        public static string TagsMerged() => "tags.merged";
        public static string TagsUpdated() => "tags.updated";
        public static string TagAliasRemoved() => "tags.alias_removed";
    }
}
