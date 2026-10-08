using Common;

namespace SearchService.Core.Features.EducationDocuments;

public sealed record EducationDocumentTagUpdate(
    string DocumentId,
    EntityType EntityType,
    Guid EntityId,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<string> TagTitles);
