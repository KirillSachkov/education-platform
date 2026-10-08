using Core.Abstractions;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Preferences.Dtos;
using NotificationService.Contracts.Preferences.Requests;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Preferences.UseCases;

/// <summary>
/// PUT /notifications/preferences — обновляет Telegram/Email флаги. InApp не настраивается
/// (продуктовое решение — всегда ON). Идемпотентно: <c>UpsertFlagsAsync</c> = <c>INSERT...
/// ON CONFLICT DO UPDATE</c>, race-safe относительно concurrent <c>UserCreated</c>.
/// </summary>
public sealed class UpdateMyPreferencesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/notifications/preferences", async Task<EndpointResult<NotificationPreferenceDto>> (
                [FromBody] UpdatePreferencesRequest request,
                [FromServices] UpdateMyPreferencesHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new UpdateMyPreferencesCommand(request), cancellationToken))
            .RequireAuthorization()
            .RequireRateLimiting(NotificationRateLimitPolicies.PREFERENCES);
    }
}

public sealed record UpdateMyPreferencesCommand(UpdatePreferencesRequest Request) : ICommand;

/// <summary>
/// Валидатор для согласованности с остальными write use-case'ами в сервисе.
/// Сейчас полей-bool два — валидировать формально нечего, кроме самой обёртки request.
/// </summary>
public sealed class UpdateMyPreferencesValidator : AbstractValidator<UpdateMyPreferencesCommand>
{
    public UpdateMyPreferencesValidator()
    {
        RuleFor(x => x.Request)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired("preferences.request"));
    }
}

public sealed class UpdateMyPreferencesHandler
    : ICommandHandler<NotificationPreferenceDto, UpdateMyPreferencesCommand>
{
    private readonly IUserChannelsRepository _userChannels;
    private readonly IUserOptOutsRepository _userOptOuts;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateMyPreferencesCommand> _validator;

    public UpdateMyPreferencesHandler(
        IUserChannelsRepository userChannels,
        IUserOptOutsRepository userOptOuts,
        UserScopedData user,
        IValidator<UpdateMyPreferencesCommand> validator)
    {
        _userChannels = userChannels;
        _userOptOuts = userOptOuts;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<NotificationPreferenceDto, Error>> Handle(
        UpdateMyPreferencesCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        UpdatePreferencesRequest request = command.Request;

        await _userChannels.UpsertFlagsAsync(
            userId: _user.UserId,
            telegramEnabled: request.TelegramEnabled,
            emailEnabled: request.EmailEnabled,
            webPushEnabled: request.WebPushEnabled,
            cancellationToken: cancellationToken);

        // Фильтрация мусорных short-кодов: в БД попадают только валидные NotificationType-значения.
        // Неизвестные игнорируются (forward-compatibility: старый клиент → новый сервер).
        HashSet<NotificationType> validOptedOut = [];
        foreach (short code in request.OptedOutTypes)
        {
            // Generic Enum.IsDefined<TEnum> — не падает на несовпадении underlying-типа.
            // Старое Enum.IsDefined(typeof(NotificationType), code) кидало ArgumentException:
            // code — Int16, а underlying у NotificationType — Int32 (баг #689).
            NotificationType candidate = (NotificationType)code;
            if (Enum.IsDefined(candidate))
                validOptedOut.Add(candidate);
        }

        await _userOptOuts.ReplaceAsync(_user.UserId, validOptedOut, cancellationToken);

        // Читаем обратно из БД — чтобы response отражал реально сохранённое состояние
        // (даже если триггер/default переопределит — мы вернём правду).
        UserNotificationChannels? saved = await _userChannels.GetByUserIdAsync(_user.UserId, cancellationToken);
        UserNotificationChannels effective = saved ?? UserNotificationChannels.Default(_user.UserId);

        List<short> optedOutShorts = new(validOptedOut.Count);
        foreach (NotificationType t in validOptedOut)
            optedOutShorts.Add((short)t);
        optedOutShorts.Sort();

        return new NotificationPreferenceDto
        {
            TelegramEnabled = effective.TelegramEnabled,
            EmailEnabled = effective.EmailEnabled,
            WebPushEnabled = effective.WebPushEnabled,
            OptedOutTypes = optedOutShorts,
        };
    }
}
