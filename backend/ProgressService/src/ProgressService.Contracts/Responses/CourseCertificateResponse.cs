namespace ProgressService.Contracts.Responses;

/// <summary>
///     Сертификат о прохождении курса. <c>CourseTitle</c>/<c>HolderName</c> — снапшоты
///     на момент выдачи (durable-документ, читается без походов в ECS/Auth). Issue #467.
/// </summary>
public sealed record CourseCertificateResponse(
    Guid Id,
    string SerialNumber,
    string HolderName,
    string CourseTitle,
    Guid CourseId,
    DateTime IssuedAt);
