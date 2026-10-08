namespace FileService.Domain;

public enum AssetUsageType
{
    AVATAR = 1,
    COURSE_PREVIEW = 2,
    MARKDOWN_IMAGE = 3,
    COURSE_VIDEO = 5,
    MARKDOWN_FILE = 7,
    MATERIAL_VIDEO = 8,
    MATERIAL_PREVIEW = 9,
    COLLECTION_COVER = 10,
}

public static class AssetUsageTypeExtensions
{
    public static Result<AssetUsageType, Error> FromString(string value, string fieldName = "usageType")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid(fieldName);
        }

        string normalized = value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "avatar" => AssetUsageType.AVATAR,
            "course_preview" or "coursepreview" or "course-preview" => AssetUsageType.COURSE_PREVIEW,
            "markdown_image" or "markdownimage" or "markdown-image" => AssetUsageType.MARKDOWN_IMAGE,
            "markdown_file" or "markdownfile" or "markdown-file" => AssetUsageType.MARKDOWN_FILE,
            "course_video" or "coursevideo" or "course-video" => AssetUsageType.COURSE_VIDEO,
            "material_video" or "materialvideo" or "material-video" => AssetUsageType.MATERIAL_VIDEO,
            "material_preview" or "materialpreview" or "material-preview" => AssetUsageType.MATERIAL_PREVIEW,
            "collection_cover" or "collectioncover" or "collection-cover" => AssetUsageType.COLLECTION_COVER,
            _ => GeneralErrors.ValueIsInvalid(fieldName),
        };
    }

    public static string ToApiString(this AssetUsageType usageType) =>
        usageType switch
        {
            AssetUsageType.AVATAR => "avatar",
            AssetUsageType.COURSE_PREVIEW => "course_preview",
            AssetUsageType.MARKDOWN_IMAGE => "markdown_image",
            AssetUsageType.MARKDOWN_FILE => "markdown_file",
            AssetUsageType.COURSE_VIDEO => "course_video",
            AssetUsageType.MATERIAL_VIDEO => "material_video",
            AssetUsageType.MATERIAL_PREVIEW => "material_preview",
            AssetUsageType.COLLECTION_COVER => "collection_cover",
            _ => throw new ArgumentOutOfRangeException(nameof(usageType)),
        };
}
