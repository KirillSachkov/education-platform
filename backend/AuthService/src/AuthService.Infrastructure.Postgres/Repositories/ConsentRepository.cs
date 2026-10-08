using AuthService.Core.Database;
using AuthService.Domain;

namespace AuthService.Infrastructure.Postgres.Repositories;

public sealed class ConsentRepository : IConsentRepository
{
    private readonly AuthDbContext _context;

    public ConsentRepository(AuthDbContext context) => _context = context;

    public async Task AddAsync(UserConsent consent, CancellationToken ct)
    {
        await _context.UserConsents.AddAsync(consent, ct);
    }

    public async Task<IReadOnlyList<UserConsent>> GetByUserAsync(Guid userId, CancellationToken ct) =>
        await _context.UserConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.AcceptedAt)
            .ToListAsync(ct);
}
