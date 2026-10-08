namespace ProgressService.Domain.Certificates;

/// <summary>
///     Сертификат о прохождении курса. Выдаётся один раз на пару (UserId, CourseId)
///     при 100% завершении курса. <c>CourseTitle</c> и <c>HolderName</c> — снапшоты на
///     момент выдачи: сертификат — durable-документ, публичная страница проверки не
///     ходит в ECS/Auth на чтении, и сертификат переживает удаление курса (каскада в
///     <c>CourseHardDeletedHandler</c> нет намеренно). <c>SerialNumber</c> детерминированно
///     выводится из Id (первые 8 байт в hex). Issue #467.
/// </summary>
public sealed class CourseCertificate
{
    public const int MAX_COURSE_TITLE_LENGTH = 500;
    public const int MAX_HOLDER_NAME_LENGTH = 200;

    private const string SERIAL_PREFIX = "CERT-";

    private CourseCertificate(Guid userId, Guid courseId, string courseTitle, string holderName)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        CourseId = courseId;
        SerialNumber = BuildSerialNumber(Id);
        CourseTitle = courseTitle;
        HolderName = holderName;
        IssuedAt = DateTime.UtcNow;
    }

    private CourseCertificate()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CourseId { get; private set; }

    public string SerialNumber { get; private set; } = string.Empty;

    public string CourseTitle { get; private set; } = string.Empty;

    public string HolderName { get; private set; } = string.Empty;

    public DateTime IssuedAt { get; private set; }

    public static Result<CourseCertificate, Error> Create(
        Guid userId,
        Guid courseId,
        string courseTitle,
        string holderName)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (courseId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(courseId));
        }

        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return GeneralErrors.ValueIsRequired(nameof(courseTitle));
        }

        if (string.IsNullOrWhiteSpace(holderName))
        {
            return GeneralErrors.ValueIsRequired(nameof(holderName));
        }

        // Снапшоты defensively обрезаются до лимитов колонок — клейм не должен падать
        // из-за длинного title/имени (реальные источники и так короче лимитов).
        string normalizedTitle = Truncate(courseTitle.Trim(), MAX_COURSE_TITLE_LENGTH);
        string normalizedHolder = Truncate(holderName.Trim(), MAX_HOLDER_NAME_LENGTH);

        return new CourseCertificate(userId, courseId, normalizedTitle, normalizedHolder);
    }

    private static string BuildSerialNumber(Guid id) =>
        SERIAL_PREFIX + id.ToString("N")[..16].ToUpperInvariant();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
