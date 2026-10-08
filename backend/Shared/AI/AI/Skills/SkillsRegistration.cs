using Microsoft.Extensions.DependencyInjection;

namespace Shared.AI.Skills;

public static class SkillsRegistration
{
    /// <summary>
    ///     Регистрирует default-реализации reusable AI Skills (<see cref="ISummarizer"/>,
    ///     <see cref="IStructuredExtractor{T}"/>, <see cref="IClassifier{T}"/>) поверх <see cref="IAiClient"/>.
    ///     Вызывать ПОСЛЕ <c>services.AddAi(...)</c>. Сервис может перерегистрировать конкретный
    ///     skill своей custom-реализацией — последняя регистрация выигрывает.
    /// </summary>
    public static IServiceCollection AddAiSkills(this IServiceCollection services)
    {
        services.AddScoped<ISummarizer, Summarizer>();
        services.AddScoped(typeof(IStructuredExtractor<>), typeof(StructuredExtractor<>));
        services.AddScoped(typeof(IClassifier<>), typeof(Classifier<>));
        return services;
    }
}
