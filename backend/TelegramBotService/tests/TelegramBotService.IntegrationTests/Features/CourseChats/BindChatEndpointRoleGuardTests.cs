using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Authorization;
using TelegramBotService.Core.Features.CourseChats.UseCases;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// Reflective regression — гарантирует, что <see cref="BindChatEndpoint.MapEndpoint"/>
/// продолжает требовать роль ADMIN. Если кто-то случайно уберёт <c>.RequireAnyRole(...)</c>
/// — этот тест упадёт.
/// </summary>
public sealed class BindChatEndpointRoleGuardTests
{
    [Fact]
    public void MapEndpoint_RegistersAuthorizationPolicyRequiringAdminRole()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorizationBuilder();

        WebApplication app = builder.Build();

        BindChatEndpoint endpoint = new();
        endpoint.MapEndpoint(app);

        // Гоняем data sources, чтобы материализовать endpoint'ы.
        EndpointDataSource dataSource = ((IEndpointRouteBuilder)app).DataSources.First();
        Endpoint registered = Assert.Single(dataSource.Endpoints);

        IAuthorizeData? authorize = registered.Metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authorize);

        // Имя policy = "AnyRole:" + roles.join(',') (см. EndpointExtensions.RequireAnyRole).
        string expected = $"AnyRole:{PlatformRoles.ADMIN}";
        Assert.Equal(expected, authorize!.Policy);
    }
}
