using Core.Abstractions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Services;

namespace ProgressService.Core;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjectionExtensions).Assembly);
        services.AddHandlers(typeof(DependencyInjectionExtensions).Assembly);

        services.AddHybridCache();
        services.AddScoped<IModuleProgressService, ModuleProgressService>();
        services.AddScoped<IEnrollmentAnchorService, EnrollmentAnchorService>();
        services.AddScoped<IStaffIssueCompletionService, StaffIssueCompletionService>();

        return services;
    }
}