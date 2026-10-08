using System.Linq.Expressions;
using ProgressService.Domain.Certificates;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий сертификатов о прохождении курса (<see cref="CourseCertificate"/>).
///     Один сертификат на пару (UserId, CourseId); снапшоты делают запись durable —
///     каскадов на удаление курса нет намеренно. Issue #467.
/// </summary>
public interface ICourseCertificateRepository
{
    Task AddAsync(CourseCertificate certificate, CancellationToken cancellationToken = default);

    Task<CourseCertificate?> GetByAsync(
        Expression<Func<CourseCertificate, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CourseCertificate>> GetManyByAsync(
        Expression<Func<CourseCertificate, bool>> predicate,
        CancellationToken cancellationToken = default);
}
