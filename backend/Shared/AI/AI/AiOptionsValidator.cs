using Microsoft.Extensions.Options;

namespace Shared.AI;

public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options) =>
        Validate(options);

    public static ValidateOptionsResult Validate(AiOptions options) =>
        ValidateInternal(options, AiOptions.SECTION_NAME);

    internal static ValidateOptionsResult ValidateInternal(AiOptions options, string sectionPath)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return ValidateOptionsResult.Fail($"{sectionPath}:ApiKey is required");

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return ValidateOptionsResult.Fail($"{sectionPath}:BaseUrl is required");

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            return ValidateOptionsResult.Fail($"{sectionPath}:BaseUrl must be an absolute URL");

        if (options.TimeoutSeconds is <= 0)
            return ValidateOptionsResult.Fail($"{sectionPath}:TimeoutSeconds must be greater than 0");

        return ValidateOptionsResult.Success;
    }
}

public sealed class AiProvidersOptionsValidator : IValidateOptions<AiProvidersOptions>
{
    public ValidateOptionsResult Validate(string? name, AiProvidersOptions options) =>
        Validate(options);

    public static ValidateOptionsResult Validate(AiProvidersOptions options)
    {
        IReadOnlyDictionary<string, AiOptions> effective = options.GetEffectiveProviders();
        if (effective.Count == 0)
        {
            return ValidateOptionsResult.Fail(
                $"At least one AI provider must be configured. Set {AiProvidersOptions.SECTION_NAME}:Providers " +
                $"or legacy single-provider fields ({AiProvidersOptions.SECTION_NAME}:Kind/BaseUrl/ApiKey).");
        }

        string defaultName = string.IsNullOrWhiteSpace(options.Default)
            ? AiProvidersOptions.DEFAULT_PROVIDER_NAME
            : options.Default;
        if (!effective.ContainsKey(defaultName))
        {
            return ValidateOptionsResult.Fail(
                $"{AiProvidersOptions.SECTION_NAME}:Default = '{defaultName}', " +
                $"but '{defaultName}' is not in Providers (configured: {string.Join(", ", effective.Keys)}).");
        }

        foreach ((string name, AiOptions providerOptions) in effective)
        {
            if (string.IsNullOrWhiteSpace(providerOptions.Kind))
            {
                return ValidateOptionsResult.Fail(
                    $"{AiProvidersOptions.SECTION_NAME}:Providers:{name}:Kind is required");
            }

            ValidateOptionsResult inner = AiOptionsValidator.ValidateInternal(
                providerOptions,
                $"{AiProvidersOptions.SECTION_NAME}:Providers:{name}");
            if (inner.Failed)
                return inner;
        }

        return ValidateOptionsResult.Success;
    }
}
