namespace Shared.Messaging.IntegrationEvents.Auth;

public static class AuthEventsRouting
{
    public const string EXCHANGE = "auth.events";

    public static class RoutingKeys
    {
        public static string UserCreated() => "user.created";
        public static string UserLoggedIn() => "user.logged_in";
        public static string UserUsernameUpdated() => "user.username_updated";
        public static string UserAvatarUpdated() => "user.avatar_updated";
        public static string UserDisplayNameUpdated() => "user.display_name_updated";
        public static string UserGithubLogin() => "user.github_login";
        public static string UserTelegramLinked() => "user.telegram_linked";
        public static string UserTelegramUnlinked() => "user.telegram_unlinked";
    }
}
