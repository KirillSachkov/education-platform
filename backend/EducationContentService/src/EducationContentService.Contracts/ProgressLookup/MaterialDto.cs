namespace EducationContentService.Contracts.ProgressLookup;

/// <summary>
///     Легковесный DTO материала для сервиса прогресса (service-to-service).
/// </summary>
public sealed record MaterialDto(Guid ModuleId, int ModuleItemsTotal);
