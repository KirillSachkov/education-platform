namespace Shared.Messaging.IntegrationEvents.Telegram;

public static class TelegramEventsRouting
{
    public const string EXCHANGE = "telegram.events";

    public static class RoutingKeys
    {
        public static string ChatBindingBoundToPlan() => "chat_binding.bound_to_plan";

        public static string ChatBindingUnboundFromPlan() => "chat_binding.unbound_from_plan";

        public static string ChatMemberConfirmed() => "chat_member.confirmed";
    }
}
