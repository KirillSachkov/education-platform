using System.Linq.Expressions;
using EducationContentService.Domain.Modules;

namespace EducationContentService.Core.Features.ModuleItems;

public interface IModulesRepository
{
    Task AddAsync(Module module, CancellationToken cancellationToken = default);

    Task<Result<Module, Error>> GetByAsync(
        Expression<Func<Module, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает id курса, к которому принадлежит модуль через
    ///     <c>course_items(reference_id=moduleId, item_type='Module')</c>.
    ///     <c>null</c>, если модуль orphan (не привязан к курсу).
    /// </summary>
    Task<Guid?> GetCourseIdAsync(Guid moduleId, CancellationToken cancellationToken = default);
}
