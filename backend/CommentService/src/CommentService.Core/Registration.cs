using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommentService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHandlers(typeof(Registration).Assembly);

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);

        services.AddAuthServiceHttpCommunication(configuration, enableCaching: true);
        services.AddEducationServiceHttpCommunication(configuration);

        return services;
    }
}