using CSharpFunctionalExtensions;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Npgsql;
using SharedKernel;

using EcsVideoChapterDto = EducationContentService.Contracts.Materials.VideoChapterDto;
using EcsUpdateVideoChaptersRequest = EducationContentService.Contracts.Materials.UpdateVideoChaptersRequest;

namespace MaterialProcessingService.Web.Configuration;

/// <summary>
///     Backfill глав видео из Kinescope в ECS для VIDEO-материалов, у которых
///     <c>education.materials.chapter_titles</c> пуст. Появилось когда выяснилось,
///     что best-effort шаг денормализации в <c>GenerateTimecodesJobHandler</c>
///     молча падал и job помечался COMPLETED — чапы в Kinescope есть, в БД нет,
///     фронт пуст.
///
///     Идемпотентно: ECS-handler <c>UpdateVideoChaptersHandler</c> сравнивает
///     текущее состояние через <c>SequenceEqual</c> и не публикует event если
///     совпадает.
///
///     Usage: <c>dotnet MaterialProcessingService.Web.dll backfill-chapters-from-kinescope</c>
/// </summary>
public static class BackfillChaptersFromKinescopeCli
{
    public const string CommandName = "backfill-chapters-from-kinescope";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ILogger logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("BackfillChaptersFromKinescopeCli");
        IConfiguration configuration = services.GetRequiredService<IConfiguration>();
        IFileServiceClient fileServiceClient = services.GetRequiredService<IFileServiceClient>();
        IEducationContentServiceClient ecsClient = services.GetRequiredService<IEducationContentServiceClient>();

        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        Guid[] videoIds = await GetVideoIdsWithEmptyChaptersAsync(
            connectionString, cancellationToken);
        logger.LogInformation(
            "Found {Count} PUBLISHED VIDEO materials with empty chapter_titles", videoIds.Length);

        int filled = 0;
        int skippedEmptyInKinescope = 0;
        int failed = 0;

        foreach (Guid videoId in videoIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            Result<GetVideoChaptersResponse?, Error> chaptersResult =
                await fileServiceClient.GetVideoChaptersAsync(videoId, cancellationToken);

            if (chaptersResult.IsFailure)
            {
                logger.LogWarning(
                    "FileService.GetVideoChapters failed for {VideoId}: {Error}",
                    videoId, chaptersResult.Error.GetMessage());
                failed++;
                continue;
            }

            if (chaptersResult.Value is null || chaptersResult.Value.Chapters.Count == 0)
            {
                skippedEmptyInKinescope++;
                continue;
            }

            IReadOnlyList<EcsVideoChapterDto> ecsChapters = chaptersResult.Value.Chapters
                .OrderBy(c => c.StartSeconds)
                .Select(c => new EcsVideoChapterDto(
                    c.Title,
                    (int)Math.Round(c.StartSeconds, MidpointRounding.AwayFromZero)))
                .ToArray();

            // AssetVersion поле endpoint'ом не валидируется и не сохраняется —
            // передаём свежий v7 guid просто для tracing/correlation.
            UnitResult<Error> updateResult = await ecsClient.UpdateVideoChaptersAsync(
                videoId,
                new EcsUpdateVideoChaptersRequest(Guid.CreateVersion7(), ecsChapters),
                cancellationToken);

            if (updateResult.IsFailure)
            {
                logger.LogError(
                    "ECS UpdateVideoChapters failed for {VideoId}: {Error}",
                    videoId, updateResult.Error.GetMessage());
                failed++;
                continue;
            }

            logger.LogInformation(
                "Backfilled {ChapterCount} chapters for video {VideoId}",
                ecsChapters.Count, videoId);
            filled++;
        }

        logger.LogInformation(
            "Backfill complete: {Filled} filled, {SkippedEmptyInKinescope} skipped (no chapters in Kinescope), {Failed} failed",
            filled, skippedEmptyInKinescope, failed);
    }

    private static async Task<Guid[]> GetVideoIdsWithEmptyChaptersAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        // Distinct video_id'ы по PUBLISHED VIDEO материалам с пустыми чапами.
        // Один video может быть привязан к нескольким материалам — ECS handler
        // сам сделает fan-out при обновлении.
        const string sql = """
            SELECT DISTINCT video_id
            FROM education.materials
            WHERE kind = 'VIDEO'
              AND status = 'PUBLISHED'
              AND video_id IS NOT NULL
              AND COALESCE(array_length(chapter_titles, 1), 0) = 0
            """;

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return ids.ToArray();
    }
}
