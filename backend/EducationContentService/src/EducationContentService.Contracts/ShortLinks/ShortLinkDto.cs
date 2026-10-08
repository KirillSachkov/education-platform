namespace EducationContentService.Contracts.ShortLinks;

/// <summary>Код короткой share-ссылки материала. Публичный URL — <c>/s/{code}</c>.</summary>
public sealed record ShortLinkDto(string Code);
