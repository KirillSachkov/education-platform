namespace Shared.Messaging.IntegrationEvents.Access;

public static class AccessEventsRouting
{
    public const string EXCHANGE = "access.events";

    public static class RoutingKeys
    {
        public static string PlanGrantCreated() => "plan_grant.created";
        public static string PlanGrantRevoked() => "plan_grant.revoked";
        public static string PlanGrantExpired() => "plan_grant.expired";
        public static string PlanGrantRenewed() => "plan_grant.renewed";
        public static string PlanGrantRenewalFailed() => "plan_grant.renewal_failed";
        public static string PlanGrantRenewalCancelled() => "plan_grant.renewal_cancelled";
        public static string PlanGrantRenewalResumed() => "plan_grant.renewal_resumed";
        public static string PlanGrantRenewalRefunded() => "plan_grant.renewal_refunded";
        public static string TrialExpiryApproaching() => "trial.expiry_approaching";
        public static string TgJoinReminderRequested() => "tg_join.reminder_requested";
        public static string PlanCourseBound() => "plan_course.bound";
        public static string PlanCourseUnbound() => "plan_course.unbound";
        public static string PlanEntitlementsChanged() => "plan.entitlements_changed";
        public static string PlanHardDeleted() => "plan.hard_deleted";
    }
}
