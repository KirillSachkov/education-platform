using AuthService.Core.Services;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using SharedKernel;
using static OpenIddict.Client.OpenIddictClientEvents;

namespace AuthService.IntegrationTests.Infrastructure;

internal static class GitHubOAuthPipelineTestExtensions
{
    public static void AddGitHubOAuthPipelineTestDoubles(this IServiceCollection services)
    {
        services.PostConfigure<OpenIddictClientOptions>(options =>
        {
            // X.509 development certificates use macOS Security.framework for RSA decryption,
            // which can block test runs. Symmetric test-only credentials exercise the same state
            // token validation without depending on the operating-system certificate store.
            options.EncryptionCredentials.Clear();
            options.EncryptionCredentials.Add(new EncryptingCredentials(
                new SymmetricSecurityKey(Enumerable.Repeat((byte)0x42, 32).ToArray()),
                SecurityAlgorithms.Aes256KW,
                SecurityAlgorithms.Aes256CbcHmacSha512));
            options.SigningCredentials.Clear();
            options.SigningCredentials.Add(new SigningCredentials(
                new SymmetricSecurityKey(Enumerable.Repeat((byte)0x24, 32).ToArray()),
                SecurityAlgorithms.HmacSha256));

            // Keep the real OpenIddict callback pipeline and replace only outbound GitHub I/O.
            options.Handlers.RemoveAll(descriptor =>
                descriptor.ServiceDescriptor.ServiceType.Namespace?.StartsWith(
                    "OpenIddict.Client.SystemNetHttp",
                    StringComparison.Ordinal) is true &&
                (descriptor.ServiceDescriptor.ServiceType.Name.StartsWith(
                     "SendHttpRequest",
                     StringComparison.Ordinal) ||
                 descriptor.ContextType == typeof(ExtractTokenResponseContext) ||
                 descriptor.ContextType == typeof(ExtractUserInfoResponseContext)));
            options.Handlers.RemoveAll(descriptor =>
                descriptor.ContextType == typeof(ExtractUserInfoResponseContext) &&
                descriptor.ServiceDescriptor.ServiceType.Name == "NormalizeContentType");

            options.Handlers.Add(
                OpenIddictClientHandlerDescriptor.CreateBuilder<ExtractTokenResponseContext>()
                    .UseInlineHandler(context =>
                    {
                        context.Response = new OpenIddictResponse
                        {
                            AccessToken = "test-github-access-token",
                            TokenType = "Bearer",
                        };

                        return ValueTask.CompletedTask;
                    })
                    .SetOrder(int.MinValue)
                    .Build());

            options.Handlers.Add(
                OpenIddictClientHandlerDescriptor.CreateBuilder<ExtractUserInfoResponseContext>()
                    .UseInlineHandler(context =>
                    {
                        context.Response = new OpenIddictResponse(
                            new Dictionary<string, string?>
                            {
                                ["id"] = "12345678",
                                ["login"] = "test-github-user",
                                ["preferred_username"] = "test-github-user",
                                ["name"] = "Test GitHub User",
                                ["email"] = "test-github-user@example.com",
                            });

                        return ValueTask.CompletedTask;
                    })
                    .SetOrder(int.MinValue)
                    .Build());
        });

        IGitHubOrgService gitHubOrgService = Substitute.For<IGitHubOrgService>();
        gitHubOrgService.FetchUserOrgsAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<string>, Error>(["sachkovtech"]));
        services.RemoveAll<IGitHubOrgService>();
        services.AddSingleton(gitHubOrgService);
    }
}
