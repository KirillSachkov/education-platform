using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Domain.Billing;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
///     <c>PATCH /access/billing-config</c> — админ включает/выключает приём прямой оплаты.
///     Admin-only. Upsert singleton-ряда <c>billing_config</c> (создаёт при первом флипе).
///     Без integration events — это локальная admin-настройка.
/// </summary>
public sealed class UpdateBillingConfigEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/billing-config", async Task<EndpointResult<BillingConfigDto>> (
                [FromBody] UpdateBillingConfigRequest request,
                [FromServices] UpdateBillingConfigHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UpdateBillingConfigCommand(request.IsEnabled), ct))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed record UpdateBillingConfigCommand(bool IsEnabled) : ICommand;

public sealed class UpdateBillingConfigHandler
    : ICommandHandler<BillingConfigDto, UpdateBillingConfigCommand>
{
    private readonly IBillingConfigRepository _repository;
    private readonly ITransactionManager _transactions;
    private readonly TimeProvider _time;

    public UpdateBillingConfigHandler(
        IBillingConfigRepository repository,
        ITransactionManager transactions,
        TimeProvider time)
    {
        _repository = repository;
        _transactions = transactions;
        _time = time;
    }

    public async Task<Result<BillingConfigDto, Error>> Handle(
        UpdateBillingConfigCommand command,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        BillingConfig? config = await _repository.GetAsync(cancellationToken);

        if (config is null)
        {
            config = BillingConfig.Create(command.IsEnabled, now);
            await _repository.AddAsync(config, cancellationToken);
        }
        else
        {
            config.SetEnabled(command.IsEnabled, now);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            // Гонка самого первого флипа (до существования singleton-ряда): конкурентный
            // PATCH уже вставил ряд → PK conflict у проигравшего. Ряд создан, цель PATCH
            // достигнута — возвращаем запрошенное значение идемпотентно, а не ошибку.
            if (save.Error.Type == ErrorType.CONFLICT)
            {
                return new BillingConfigDto(command.IsEnabled);
            }

            return save.Error;
        }

        return new BillingConfigDto(config.IsEnabled);
    }
}
