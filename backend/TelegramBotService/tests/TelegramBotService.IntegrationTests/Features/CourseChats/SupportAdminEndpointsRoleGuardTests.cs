using Framework.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Authorization;
using TelegramBotService.Core.Features.CourseChats.UseCases;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// Reflective regression (#444) — гарантирует, что все НОВЫЕ support/admin endpoint'ы
/// требуют роль ADMIN или MODERATOR (policy <c>AnyRole:platform-admin,platform-moderator</c>).
/// Если кто-то случайно уберёт/ослабит <c>.RequireAnyRole(...)</c> — тест упадёт, и
/// non-admin/moderator caller перестал бы блокироваться authorization-слоем.
/// </summary>
public sealed class SupportAdminEndpointsRoleGuardTests
{
    private const string EXPECTED_POLICY =
        $"AnyRole:{PlatformRoles.ADMIN},{PlatformRoles.MODERATOR}";

    public static TheoryData<IEndpoint> Endpoints() =>
        new(
            new GetAdminUserTelegramLinkEndpoint(),
            new GetAdminPlanChatsEndpoint(),
            new ResendPlanWelcomeEndpoint(),
            new ResyncUserInvitesEndpoint());

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void MapEndpoint_RequiresAdminOrModeratorRole(IEndpoint endpoint)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorizationBuilder();

        WebApplication app = builder.Build();
        endpoint.MapEndpoint(app);

        EndpointDataSource dataSource = ((IEndpointRouteBuilder)app).DataSources.First();
        Endpoint registered = Assert.Single(dataSource.Endpoints);

        IAuthorizeData? authorize = registered.Metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authorize);
        Assert.Equal(EXPECTED_POLICY, authorize!.Policy);
    }
}
