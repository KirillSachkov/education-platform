using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging;
using SearchService.Domain;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Courses;

/// <summary>
/// Пере-индексирует видимость всех материалов курса при archive/restore (#378).
/// Материал может состоять в нескольких курсах, поэтому решение о видимости
/// (<c>IsCourseOrphaned</c>) принимает ECS-lookup из полного членства, а не этот
/// каскад. Здесь — только триггер: дёрнуть lookup + upsert для каждого материала.
/// </summary>
internal static class CourseMaterialsReindexer
{
    public static async Task ReindexAsync(
        Guid courseId,
        IEducationContentServiceClient educationService,
        EducationDocumentService educationDocumentService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Result<CourseMaterialIdsDto, Error> idsResult =
            await educationService.GetCourseMaterialIdsAsync(courseId, cancellationToken);

        if (idsResult.IsFailure)
        {
            // Hard-deleted курс отдаёт 200 + пустой список (SQL не находит строк), НЕ failure —
            // поэтому failure здесь = транзиентная ошибка ECS (5xx / сеть / десериализация).
            // Бросаем, чтобы Wolverine повторил сообщение: иначе каскад тихо пропустится и
            // материалы залипнут видимыми до nightly reconciliation.
            Exception exception = idsResult.Error.ToException();
            logger.LogError(
                exception,
                "Failed to fetch material ids for course reindex. CourseId: {CourseId}",
                courseId);
            throw exception;
        }

        // Последовательный fan-out осознанный: материалов на курс — единицы/десятки,
        // archive/restore курса — редкая операция. Если курсы вырастут до сотен материалов —
        // имеет смысл batch-lookup. Пока N+1 здесь дешевле лишней batch-ручки в ECS.
        foreach (Guid materialId in idsResult.Value.MaterialIds)
        {
            Result<MaterialSearchLookupDto, Error> lookupResult =
                await educationService.GetMaterialSearchLookupAsync(materialId, cancellationToken);

            if (lookupResult.IsFailure)
            {
                logger.LogWarning(
                    "Failed to fetch material lookup during course reindex. CourseId: {CourseId}, "
                    + "MaterialId: {MaterialId}, Error: {Error}",
                    courseId,
                    materialId,
                    lookupResult.Error);
                continue;
            }

            EducationDocument document = EducationDocumentFactory.FromMaterial(lookupResult.Value);
            UnitResult<Error> upsertResult =
                await educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

            if (upsertResult.IsFailure)
            {
                Exception exception = upsertResult.Error.ToException();
                logger.LogError(
                    exception,
                    "Failed to reindex material visibility during course reindex. CourseId: {CourseId}, MaterialId: {MaterialId}",
                    courseId,
                    materialId);
                throw exception;
            }
        }

        logger.LogInformation(
            "Reindexed {Count} course materials for visibility. CourseId: {CourseId}",
            idsResult.Value.MaterialIds.Count,
            courseId);
    }
}
