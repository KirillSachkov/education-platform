using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NotificationService.Contracts.Broadcast.Requests;
using NotificationService.Core.Database;
using NotificationService.Domain.Subscriptions;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using SharedKernel;

namespace NotificationService.Core.Features.Broadcast.UseCases;

/// <summary>
/// Ручная рассылка автора подписчикам курса / Author broadcast to course subscribers.
/// Endpoint публикует <see cref="NotificationBroadcastRequested"/> и сразу возвращает ответ —
/// реальную развёртку в N уведомлений делает фоновый <c>BroadcastFanoutHandler</c>.
/// </summary>
public sealed record BroadcastNotificationCommand(BroadcastNotificationRequest Request) : ICommand;

public sealed class BroadcastNotificationValidator : AbstractValidator<BroadcastNotificationCommand>
{
    public BroadcastNotificationValidator()
    {
        RuleFor(x => x.Request.TargetType)
            .NotEmpty()
            .Must(SubscriptionEntityType.IsValid)
            .WithError(Error.Validation(
                "notifications.broadcast.target_type",
                "Допустимые типы: course, author, module"));

        RuleFor(x => x.Request.TargetId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetId"));

        RuleFor(x => x.Request.Title)
            .NotEmpty()
            .MaximumLength(200)
            .WithError(GeneralErrors.ValueIsRequired("title"));

        RuleFor(x => x.Request.Body)
            .NotEmpty()
            .MaximumLength(5000)
            .WithError(GeneralErrors.ValueIsRequired("body"));
    }
}

public sealed class BroadcastNotificationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/broadcast",
                async Task<EndpointResult<BroadcastNotificationResponse>> (
                    [FromBody] BroadcastNotificationRequest request,
                    [FromServices] BroadcastNotificationHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new BroadcastNotificationCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE)
            .RequireRateLimiting(NotificationRateLimitPolicies.BROADCAST);
    }
}

public sealed class BroadcastNotificationHandler
    : ICommandHandler<BroadcastNotificationResponse, BroadcastNotificationCommand>
{
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ISubscribersQuery _subscribers;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly IValidator<BroadcastNotificationCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<BroadcastNotificationHandler> _logger;

    public BroadcastNotificationHandler(
        IEducationContentServiceClient ecsClient,
        ISubscribersQuery subscribers,
        IOutboxService outbox,
        ITransactionManager transactions,
        IValidator<BroadcastNotificationCommand> validator,
        UserScopedData user,
        ILogger<BroadcastNotificationHandler> logger)
    {
        _ecsClient = ecsClient;
        _subscribers = subscribers;
        _outbox = outbox;
        _transactions = transactions;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<BroadcastNotificationResponse, Error>> Handle(
        BroadcastNotificationCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        BroadcastNotificationRequest request = command.Request;

        // Ownership check для course-broadcast. Admin — bypass. Для author-broadcast разрешаем,
        // только если TargetId == user.UserId (свой space). Для module — Phase 4+ (нужно резолвить
        // Module → Course → Author через ECS; пока не поддерживается).
        if (!_user.IsAdmin)
        {
            UnitResult<Error> ownership = await CheckOwnership(request, cancellationToken);
            if (ownership.IsFailure)
                return ownership.Error;
        }

        int estimatedRecipients = await _subscribers.CountByEntityAsync(
            request.TargetType, request.TargetId, cancellationToken);

        Guid broadcastId = Guid.CreateVersion7();

        await _outbox.PublishAsync(new NotificationBroadcastRequested(
            BroadcastId: broadcastId,
            RequestedByUserId: _user.UserId,
            TargetType: request.TargetType,
            TargetId: request.TargetId,
            TemplateId: "author.announcement",
            Title: request.Title,
            Body: request.Body,
            Channels: request.Channels ?? 0,   // 0 = dispatcher возьмёт политику
            PayloadJson: "{}",
            RequestedAt: DateTimeOffset.UtcNow));

        // Endpoint не открывает свою транзакцию (только outbox-publish, без entity-changes),
        // но ITransactionManager.SaveChangesAsync без активной транзакции звёт
        // _outbox.SaveChangesAndFlushMessagesAsync — это выгружает буферизованные publish'и
        // в wolverine_outgoing_envelopes и пинает relay. Без этого DbContext дропается, событие
        // теряется, BroadcastFanoutHandler никогда не запускается.
        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            _logger.LogError(
                "Failed to flush broadcast publish for {BroadcastId}: {Error}",
                broadcastId, save.Error);
            return save.Error;
        }

        _logger.LogInformation(
            "Broadcast requested: Id={BroadcastId} Target={TargetType}:{TargetId} By={UserId} EstimatedRecipients={Count}",
            broadcastId, request.TargetType, request.TargetId, _user.UserId, estimatedRecipients);

        return new BroadcastNotificationResponse(broadcastId, estimatedRecipients);
    }

    private async Task<UnitResult<Error>> CheckOwnership(
        BroadcastNotificationRequest request,
        CancellationToken ct)
    {
        if (string.Equals(request.TargetType, SubscriptionEntityType.AUTHOR, StringComparison.Ordinal))
        {
            if (request.TargetId != _user.UserId)
                return Error.Authorization(
                    "notifications.broadcast.not_author",
                    "Можно отправлять рассылку только от имени собственного пространства автора");
            return UnitResult.Success<Error>();
        }

        if (string.Equals(request.TargetType, SubscriptionEntityType.COURSE, StringComparison.Ordinal))
        {
            Result<CourseDto, Error> courseResult = await _ecsClient.GetCourseLookupAsync(request.TargetId, ct);
            if (courseResult.IsFailure)
                return courseResult.Error;

            if (courseResult.Value.AuthorId != _user.UserId)
                return Error.Authorization(
                    "notifications.broadcast.not_course_owner",
                    "Вы не автор этого курса");

            return UnitResult.Success<Error>();
        }

        // module — пока не поддерживаем
        return Error.Validation(
            "notifications.broadcast.target_type_unsupported",
            $"Тип '{request.TargetType}' пока не поддерживается для рассылки");
    }
}
