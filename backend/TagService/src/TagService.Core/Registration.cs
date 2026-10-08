using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TagService.Core.Features.Tags;

namespace TagService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHandlers(typeof(Registration).Assembly);

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);
        services.AddEducationServiceHttpCommunication(configuration);
        services.AddScoped<EntityTagAuthorization>();

        return services;
    }
}
