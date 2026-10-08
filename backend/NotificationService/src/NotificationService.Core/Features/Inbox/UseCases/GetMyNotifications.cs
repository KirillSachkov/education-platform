using Core.Abstractions;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NotificationService.Contracts.Inbox.Dtos;
using NotificationService.Contracts.Inbox.Requests;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using PlatformAuth.Middleware;
using Shared.Navigation;
using SharedKernel;

namespace NotificationService.Core.Features.Inbox.UseCases;

public sealed class GetMyNotificationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications", async Task<EndpointResult<NotificationListResponse>> (
                [FromQuery] int? limit,
                [FromQuery] DateTime? cursorBefore,
                [FromQuery] Guid? cursorId,
                [FromQuery] bool? unreadOnly,
                [FromQuery] short[]? types,
                [FromServices] GetMyNotificationsHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(
                new GetMyNotificationsQuery(new GetNotificationsRequest
                {
                    Limit = limit ?? 20,
                    CursorBefore = cursorBefore,
                    CursorId = cursorId,
                    UnreadOnly = unreadOnly ?? false,
                    Types = types is { Length: > 0 } ? types : null,
                }),
                cancellationToken))
            .RequireAuthorization();
    }
}

public sealed class GetMyNotificationsValidator : AbstractValidator<GetMyNotificationsQuery>
{
    public GetMyNotificationsValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, 100)
            .WithError(Error.Validation("notifications.limit", "Размер страницы должен быть от 1 до 100"));

        RuleFor(x => x.Request)
            .Must(request => request.CursorBefore.HasValue == request.CursorId.HasValue)
            .WithError(Error.Validation(
                "notifications.cursor.incomplete",
                "CursorBefore и CursorId должны быть заданы вместе"));

        RuleFor(x => x.Request.CursorId)
            .Must(cursorId => !cursorId.HasValue || cursorId.Value != Guid.Empty)
            .WithError(Error.Validation(
                "notifications.cursor.id",
                "CursorId не может быть пустым GUID"));

        RuleFor(x => x.Request.Types)
            .Must(types => types is null || types.Count <= 32)
            .WithError(Error.Validation(
                "notifications.types.limit",
                "Можно отфильтровать не более 32 типов уведомлений"));

        RuleFor(x => x.Request.Types)
            .Must(types => types is null || types.All(type => Enum.IsDefined(typeof(NotificationType), (int)type)))
            .WithError(Error.Validation(
                "notifications.types.invalid",
                "Запрос содержит неизвестный тип уведомления"));
    }
}

public sealed record GetMyNotificationsQuery(GetNotificationsRequest Request) : IQuery;

public sealed class GetMyNotificationsHandler
    : IQueryHandlerWithResult<NotificationListResponse, GetMyNotificationsQuery>
{
    private readonly INotificationsRepository _notificationsRepository;
    private readonly UserScopedData _user;
    private readonly IValidator<GetMyNotificationsQuery> _validator;
    private readonly NotificationOptions _options;

    public GetMyNotificationsHandler(
        INotificationsRepository notificationsRepository,
        UserScopedData user,
        IValidator<GetMyNotificationsQuery> validator,
        IOptions<NotificationOptions> options)
    {
        _notificationsRepository = notificationsRepository;
        _user = user;
        _validator = validator;
        _options = options.Value;
    }

    public async Task<Result<NotificationListResponse, Error>> Handle(
        GetMyNotificationsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        int limit = query.Request.Limit;

        IReadOnlyList<Notification> notifications = await _notificationsRepository.ListForUserAsync(
            _user.UserId,
            limit,
            query.Request.CursorBefore,
            query.Request.CursorId,
            query.Request.UnreadOnly,
            query.Request.Types,
            cancellationToken);

        NotificationDto[] items = new NotificationDto[notifications.Count];
        for (int i = 0; i < notifications.Count; i++)
        {
            Notification n = notifications[i];
            items[i] = new NotificationDto
            {
                Id = n.Id.Value,
                Type = (short)n.Type,
                TemplateId = n.TemplateId,
                Title = n.Title,
                Body = n.Body,
                Payload = n.Payload,
                TargetUrl = PlatformLinkBuilder.ResolveTargetUrl(
                    _options.FrontendBaseUrl,
                    (short)n.Type,
                    n.Payload),
                Channels = (short)n.Channels,
                CreatedAt = new DateTimeOffset(n.CreatedAt, TimeSpan.Zero),
                ReadAt = n.ReadAt.HasValue ? new DateTimeOffset(n.ReadAt.Value, TimeSpan.Zero) : null,
                CorrelationId = n.CorrelationId,
            };
        }

        DateTimeOffset? nextCursorBefore = null;
        Guid? nextCursorId = null;

        if (items.Length == limit)
        {
            NotificationDto last = items[^1];
            nextCursorBefore = last.CreatedAt;
            nextCursorId = last.Id;
        }

        return new NotificationListResponse
        {
            Items = items,
            NextCursorBefore = nextCursorBefore,
            NextCursorId = nextCursorId,
        };
    }
}
