using System.Linq.Expressions;
using ServiceName.Core.Database;
using ServiceName.Domain;
using ServiceName.Domain.Widgets;

namespace ServiceName.Infrastructure.Postgres.Repositories;

internal sealed class WidgetsRepository : IWidgetsRepository
{
    private readonly ServiceNameDbContext _dbContext;

    public WidgetsRepository(ServiceNameDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Widget widget, CancellationToken ct = default) =>
        await _dbContext.Widgets.AddAsync(widget, ct);

    public async Task<Result<Widget, Error>> GetByAsync(
        Expression<Func<Widget, bool>> predicate,
        CancellationToken ct = default)
    {
        Widget? widget = await _dbContext.Widgets.FirstOrDefaultAsync(predicate, ct);
        return widget is null
            ? ServiceNameErrors.Widget.NotFound(Guid.Empty)
            : widget;
    }

    public async Task<IReadOnlyList<Widget>> GetManyByAsync(
        Expression<Func<Widget, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.Widgets.Where(predicate).ToListAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<Widget, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.Widgets.AnyAsync(predicate, ct);
}
