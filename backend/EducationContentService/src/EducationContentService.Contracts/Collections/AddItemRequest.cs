namespace EducationContentService.Contracts.Collections;

/// <summary>
///     Добавление элемента в секцию подборки (#491 — generic items).
/// </summary>
/// <param name="ReferenceId">Id материала или квиза.</param>
/// <param name="ItemType">
///     <c>MATERIAL</c> (default при <c>null</c>/пустом — back-compat со старыми
///     payload'ами) или <c>QUIZ</c>.
/// </param>
public sealed record AddItemRequest(Guid ReferenceId, string? ItemType = null);
