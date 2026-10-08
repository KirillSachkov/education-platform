namespace FileService.Domain;

public static class AssetUsagePolicyCatalog
{
    private static readonly IReadOnlyDictionary<AssetUsageType, AssetUsagePolicy> _policies =
        new Dictionary<AssetUsageType, AssetUsagePolicy>
        {
            [AssetUsageType.AVATAR] = new(
                AssetKind.FILE,
                AssetRegistrationMode.EntityRequired,
                5 * 1024 * 1024,
                ["image/jpeg", "image/png", "image/webp"],
                new Dictionary<string, string>
                {
                    ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp",
                },
                ["user", "profile"]),
            [AssetUsageType.COURSE_PREVIEW] = new(
                AssetKind.FILE,
                AssetRegistrationMode.EntityRequired,
                10 * 1024 * 1024,
                ["image/jpeg", "image/png", "image/webp"],
                new Dictionary<string, string>
                {
                    ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp",
                },
                ["course"]),
            [AssetUsageType.MARKDOWN_IMAGE] = new(
                AssetKind.FILE,
                AssetRegistrationMode.DraftOrEntity,
                10 * 1024 * 1024,
                ["image/jpeg", "image/png", "image/webp"],
                new Dictionary<string, string>
                {
                    ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp",
                },
                ["issue", "material", "project", "plan_onboarding_step"]),
            // MARKDOWN_FILE: arbitrary file attachments inside material/issue markdown
            // bodies. MIME whitelist below MUST be kept in sync with the frontend
            // hook `frontend/src/entities/file/model/use-markdown-file-upload.ts`.
            [AssetUsageType.MARKDOWN_FILE] = new(
                AssetKind.FILE,
                AssetRegistrationMode.DraftOrEntity,
                25 * 1024 * 1024,
                [
                    "application/pdf", "text/plain", "text/csv", "text/markdown",
                    "application/json", "application/vnd.excalidraw+json",
                    "application/zip", "application/x-tar", "application/x-gzip",
                    "application/msword",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "application/vnd.ms-excel",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "application/vnd.ms-powerpoint",
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ],
                new Dictionary<string, string>
                {
                    ["application/pdf"] = "pdf",
                    ["text/plain"] = "txt",
                    ["text/csv"] = "csv",
                    ["text/markdown"] = "md",
                    ["application/json"] = "json",
                    ["application/vnd.excalidraw+json"] = "excalidraw",
                    ["application/zip"] = "zip",
                    ["application/x-tar"] = "tar",
                    ["application/x-gzip"] = "gz",
                    ["application/msword"] = "doc",
                    ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "docx",
                    ["application/vnd.ms-excel"] = "xls",
                    ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "xlsx",
                    ["application/vnd.ms-powerpoint"] = "ppt",
                    ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "pptx",
                },
                ["issue", "material", "project", "plan_onboarding_step"]),
            [AssetUsageType.COURSE_VIDEO] = new(
                AssetKind.VIDEO,
                AssetRegistrationMode.EntityRequired,
                5L * 1024 * 1024 * 1024,
                ["video/mp4", "video/webm", "video/quicktime", "video/x-matroska", "video/avi"],
                new Dictionary<string, string>
                {
                    ["video/mp4"] = "mp4",
                    ["video/webm"] = "webm",
                    ["video/quicktime"] = "mov",
                    ["video/x-matroska"] = "mkv",
                    ["video/avi"] = "avi",
                },
                ["course"]),
            [AssetUsageType.MATERIAL_VIDEO] = new(
                AssetKind.VIDEO,
                AssetRegistrationMode.DraftOrEntity,
                5L * 1024 * 1024 * 1024,
                ["video/mp4", "video/webm", "video/quicktime", "video/x-matroska", "video/avi"],
                new Dictionary<string, string>
                {
                    ["video/mp4"] = "mp4",
                    ["video/webm"] = "webm",
                    ["video/quicktime"] = "mov",
                    ["video/x-matroska"] = "mkv",
                    ["video/avi"] = "avi",
                },
                ["material"]),
            [AssetUsageType.MATERIAL_PREVIEW] = new(
                AssetKind.FILE,
                AssetRegistrationMode.DraftOrEntity,
                10 * 1024 * 1024,
                ["image/jpeg", "image/png", "image/webp"],
                new Dictionary<string, string>
                {
                    ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp",
                },
                ["material"]),
            [AssetUsageType.COLLECTION_COVER] = new(
                AssetKind.FILE,
                AssetRegistrationMode.EntityRequired,
                10 * 1024 * 1024,
                ["image/jpeg", "image/png", "image/webp"],
                new Dictionary<string, string>
                {
                    ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp",
                },
                ["collection"]),
        };

    /// <summary>
    ///     Определяет, является ли тип использования одиночным (один ресурс на сущность).
    ///     Такие типы автоматически заменяют предыдущий Ready-ресурс при привязке нового.
    /// </summary>
    public static bool IsSingleAssetPerEntity(AssetUsageType usageType) =>
        usageType is AssetUsageType.AVATAR
            or AssetUsageType.COURSE_PREVIEW
            or AssetUsageType.COURSE_VIDEO
            or AssetUsageType.MATERIAL_PREVIEW
            or AssetUsageType.MATERIAL_VIDEO
            or AssetUsageType.COLLECTION_COVER;

    public static Result<AssetUsagePolicy, Error> Get(AssetUsageType usageType)
    {
        if (_policies.TryGetValue(usageType, out AssetUsagePolicy? policy))
        {
            return policy;
        }

        return GeneralErrors.ValueIsInvalid("usageType");
    }
}
