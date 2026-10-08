using System.Data.Common;
using AuthService.Contracts.Admin;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Core.Features.Users.Queries;
using AuthService.Domain;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Admin.Queries;

public sealed record GetUserDetailQuery(Guid UserId) : IQuery;

public sealed class GetUserDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/{userId:guid}", async Task<EndpointResult<AdminUserDetailDto>> (
                    Guid userId,
                    [Microsoft.AspNetCore.Mvc.FromServices] GetUserDetailHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserDetailQuery(userId), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

public sealed class GetUserDetailHandler : IQueryHandlerWithResult<AdminUserDetailDto, GetUserDetailQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetUserDetailHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminUserDetailDto, Error>> Handle(
        GetUserDetailQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT id, user_name, email, email_confirmed, display_name,
                                  created_at, updated_at, lockout_end, last_login_at
                           FROM users
                           WHERE id = @UserId;

                           SELECT r.name
                           FROM user_roles ur
                           JOIN roles r ON r.id = ur.role_id
                           WHERE ur.user_id = @UserId;

                           SELECT bio, role_profiles AS Profiles, avatar_id AS AvatarId
                           FROM user_profiles
                           WHERE id = @UserId;

                           SELECT provider_display_name
                           FROM user_logins
                           WHERE user_id = @UserId AND login_provider = @TgProvider
                           LIMIT 1;
                           """;

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            sql, new { UserId = query.UserId, TgProvider = TelegramProviderConstants.PROVIDER_NAME });

        UserDetailRow? user = await multi.ReadFirstOrDefaultAsync<UserDetailRow>();
        if (user is null)
            return AuthErrors.UserNotFound();

        List<string> roles = (await multi.ReadAsync<string>()).ToList();

        UserProfileRow? profile = await multi.ReadFirstOrDefaultAsync<UserProfileRow>();

        // Telegram @handle (provider_display_name), пусто если Telegram не привязан — #575.
        string? telegramUsername = await multi.ReadFirstOrDefaultAsync<string?>();

        bool isLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;

        return new AdminUserDetailDto(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.EmailConfirmed,
            roles,
            isLockedOut,
            user.LockoutEnd,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastLoginAt,
            profile?.Bio,
            profile?.Profiles,
            profile?.AvatarId,
            telegramUsername);
    }

    private sealed record UserDetailRow
    {
        public Guid Id { get; init; }
        public string? UserName { get; init; }
        public string? Email { get; init; }
        public bool EmailConfirmed { get; init; }
        public string? DisplayName { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
        public DateTime? LastLoginAt { get; init; }
    }
}
