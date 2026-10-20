namespace Shared.Messaging.IntegrationEvents.Files;

public static class FileEventsRouting
{
    public const string EXCHANGE = "file.events";

    public static class EntityTypes
    {
        public const string COURSE = "course";
        public const string MATERIAL = "material";
        public const string MODULE = "module";
        public const string ISSUE = "issue";
        public const string USER = "user";
        public const string COLLECTION = "collection";
    }

    public static class UsageTypes
    {
        public const string COURSE_PREVIEW = "course_preview";
        public const string COURSE_VIDEO = "course_video";
        public const string MATERIAL_VIDEO = "material_video";
        public const string MATERIAL_PREVIEW = "material_preview";
        public const string MARKDOWN_IMAGE = "markdown_image";
        public const string MARKDOWN_FILE = "markdown_file";
        public const string AVATAR = "avatar";
        public const string COLLECTION_COVER = "collection_cover";
    }

    public static class RoutingKeys
    {
        public const string ALL_MATERIAL_EVENTS = "*.*.material";
        public const string ALL_COURSE_EVENTS = "*.*.course";
        public const string ALL_COLLECTION_EVENTS = "*.*.collection";

        public static string Bound(string entityType) =>
            $"file.bound.{NormalizeSegment(entityType, nameof(entityType))}";

        public static string Deleted(string entityType) =>
            $"file.deleted.{NormalizeSegment(entityType, nameof(entityType))}";

        public static string UploadInitiated(string entityType) =>
            $"video.upload-initiated.{NormalizeSegment(entityType, nameof(entityType))}";

        public static string Detached() => "file.detached";

        public static string BindingConfirmed() => "file.binding-confirmed";

        private static string NormalizeSegment(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Routing key segment must not be empty.", paramName);
            }

            string normalized = value.Trim().ToLowerInvariant();

            if (normalized[0] is '-' or '_' || normalized[^1] is '-' or '_')
            {
                throw new ArgumentException(
                    "Routing key segment must start and end with a letter or digit.",
                    paramName);
            }

            foreach (char c in normalized)
            {
                bool isAllowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '-' or '_';
                if (!isAllowed)
                {
                    throw new ArgumentException(
                        "Routing key segment supports only lowercase letters, digits, '-' and '_'.",
                        paramName);
                }
            }

            return normalized;
        }
    }
}