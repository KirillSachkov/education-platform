using Microsoft.Extensions.Options;

namespace AuthService.Core.Options;

public sealed class OpenIddictOptionsValidator : IValidateOptions<OpenIddictOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenIddictOptions options)
    {
        var failures = new List<string>();

        ValidateClient(options.EducationPlatform, "EducationPlatform", failures);
        ValidateClient(options.ServiceToService, "ServiceToService", failures);
        ValidateClient(options.AdminApi, "AdminApi", failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateClient(OpenIddictClientOptions client, string name, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(client.ClientId))
            failures.Add($"OpenIddict:{name}:ClientId is required");

        if (string.IsNullOrWhiteSpace(client.Secret))
            failures.Add($"OpenIddict:{name}:Secret is required");
    }
}
