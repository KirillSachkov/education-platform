using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlatformAuth.Middleware;
using SharedKernel;
using MaterialProcessingService.Core.Configuration;
using MaterialProcessingService.Contracts.ContentDrafts.Dtos;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.Features.ContentDrafts.Generate;
using MaterialProcessingService.Core.Features.Timecodes;
using MaterialProcessingService.Core.Features.Timecodes.Generate;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.IntegrationTests.Features;

public sealed class EnqueueConflictRecoveryTests
{
    [Fact]
    public async Task GenerateTimecodes_ReturnsExistingActiveJob_WhenConcurrentInsertWins()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();
        Guid uploaderUserId = Guid.CreateVersion7();

        ProcessingSourceType sourceType = ProcessingSourceType.Create("HLS").Value;
        TimecodeGenerationJob existingJob = TimecodeGenerationJob.Create(
            videoId,
            assetVersion,
            Guid.CreateVersion7(),
            sourceType).Value;

        int activeLookupCount = 0;
        ITimecodeGenerationJobRepository jobRepository = Substitute.For<ITimecodeGenerationJobRepository>();
        jobRepository.GetActiveByVideoAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                activeLookupCount++;
                return activeLookupCount == 1
                    ? null
                    : existingJob;
            });

        IFileServiceClient fileServiceClient = Substitute.For<IFileServiceClient>();
        fileServiceClient.GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<GetVideoProcessingSourceResponse?, Error>(
                new GetVideoProcessingSourceResponse(
                    videoId,
                    "READY",
                    assetVersion,
                    120,
                    "HLS",
                    $"https://video.test/{videoId}/master.m3u8",
                    DateTime.UtcNow.AddMinutes(30),
                    UploadedByUserId: uploaderUserId))));

        IOutboxService outboxService = Substitute.For<IOutboxService>();
        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        // Аутентифицируемся под тем же UserId, который владеет видео — иначе
        // BOLA-check внутри handler'а отбрасывает запрос с video.authorship.required.
        UserScopedData user = new();
        user.Authenticate(uploaderUserId, "tester", "tester@example.com", ["platform-author"]);

        IValidator<GenerateVideoTimecodesCommand> validator = Substitute.For<IValidator<GenerateVideoTimecodesCommand>>();
        validator.ValidateAsync(Arg.Any<GenerateVideoTimecodesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        // Concurrency-recovery переехал в TimecodeJobEnqueuer (issue #648) — handler
        // делегирует ему постановку, поэтому recovery тестируется через реальный enqueuer
        // с замоканными repo/outbox/transaction.
        var enqueuer = new TimecodeJobEnqueuer(jobRepository, outboxService, transactionManager);
        var handler = new GenerateVideoTimecodesHandler(
            fileServiceClient,
            enqueuer,
            user,
            new TestOptionsMonitor<AiPipelineFeatureFlags>(new AiPipelineFeatureFlags { Enabled = true }),
            validator);

        Result<GenerateVideoTimecodesResponse, Error> result = await handler.Handle(
            new GenerateVideoTimecodesCommand(videoId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingJob.Id, result.Value.JobId);
        Assert.Equal("QUEUED", result.Value.Status);
    }

    [Fact]
    public async Task GenerateContent_ReturnsExistingActiveJob_WhenConcurrentInsertWins()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();
        Guid uploaderUserId = Guid.CreateVersion7();

        ProcessingSourceType sourceType = ProcessingSourceType.Create("HLS").Value;
        ContentGenerationJob existingJob = ContentGenerationJob.Create(
            videoId,
            materialId,
            assetVersion,
            Guid.CreateVersion7(),
            sourceType).Value;

        int activeLookupCount = 0;
        IContentGenerationJobRepository jobRepository = Substitute.For<IContentGenerationJobRepository>();
        jobRepository.GetActiveByVideoAndMaterialAsync(videoId, materialId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                activeLookupCount++;
                return activeLookupCount == 1
                    ? null
                    : existingJob;
            });

        IFileServiceClient fileServiceClient = Substitute.For<IFileServiceClient>();
        fileServiceClient.GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<GetVideoProcessingSourceResponse?, Error>(
                new GetVideoProcessingSourceResponse(
                    videoId,
                    "READY",
                    assetVersion,
                    120,
                    "HLS",
                    $"https://video.test/{videoId}/master.m3u8",
                    DateTime.UtcNow.AddMinutes(30),
                    UploadedByUserId: uploaderUserId))));

        IOutboxService outboxService = Substitute.For<IOutboxService>();
        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        // Same uploaderUserId — иначе CheckOwnership отказывает.
        UserScopedData user = new();
        user.Authenticate(uploaderUserId, "tester", "tester@example.com", ["platform-author"]);

        IValidator<GenerateVideoContentRequest> validator = Substitute.For<IValidator<GenerateVideoContentRequest>>();
        validator.ValidateAsync(Arg.Any<GenerateVideoContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        IEducationContentServiceClient educationClient =
            Substitute.For<IEducationContentServiceClient>();
        // forceOverwrite=true в тесте: проверяем concurrency-recovery, не overwrite check.
        // По дефолту тесты дёргают forceOverwrite=true (см. CreateRequest helpers ниже).

        var handler = new GenerateVideoContentHandler(
            jobRepository,
            fileServiceClient,
            educationClient,
            outboxService,
            transactionManager,
            user,
            new TestOptionsMonitor<AiPipelineFeatureFlags>(new AiPipelineFeatureFlags { Enabled = true }),
            validator);

        Result<GenerateVideoContentResponse, Error> result = await handler.Handle(
            new GenerateVideoContentCommand(videoId, new GenerateVideoContentRequest(materialId, ForceOverwrite: true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingJob.Id, result.Value.JobId);
        Assert.Equal(materialId, result.Value.MaterialId);
        Assert.Equal("QUEUED", result.Value.Status);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
