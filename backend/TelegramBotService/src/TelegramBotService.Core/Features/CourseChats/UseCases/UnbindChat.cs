using Core.Abstractions;
using Core.Database;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using TelegramBotService.Core.Database;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

public sealed record UnbindChatCommand(Guid BindingId) : ICommand;

public sealed class UnbindChatEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/telegram/admin/chat-bindings/{bindingId:guid}/",
                async Task<EndpointResult<string>> (
                    [FromRoute] Guid bindingId,
                    [FromServices] UnbindChatHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UnbindChatCommand(bindingId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed class UnbindChatHandler : ICommandHandler<string, UnbindChatCommand>
{
    private readonly IChatBindingRepository _bindings;
    private readonly IChatAdministrationApi _chatApi;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _user;
    private readonly ILogger<UnbindChatHandler> _logger;

    public UnbindChatHandler(
        IChatBindingRepository bindings,
        IChatAdministrationApi chatApi,
        ITransactionManager transactions,
        IOutboxService outbox,
        UserScopedData user,
        ILogger<UnbindChatHandler> logger)
    {
        _bindings = bindings;
        _chatApi = chatApi;
        _transactions = transactions;
        _outbox = outbox;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<string, Error>> Handle(UnbindChatCommand command, CancellationToken cancellationToken)
    {
        Guid bindingId = command.BindingId;
        Result<ChatBinding, Error> bindingResult = await _bindings.GetByAsync(
            x => x.Id == bindingId, cancellationToken);

        if (bindingResult.IsFailure)
            return bindingResult.Error;

        ChatBinding binding = bindingResult.Value;

        // Best-effort: revoke invite link. Чат может быть удалён / бот выкинут — это не блокер.
        await _chatApi.RevokeChatInviteLinkAsync(binding.TelegramChatId, binding.InviteLink, cancellationToken);

        UnitResult<Error> beginResult = await _transactions.BeginTransactionAsync(cancellationToken);
        if (beginResult.IsFailure)
            return beginResult.Error;

        await _bindings.AcquirePlanMutationLockAsync(binding.PlanId, cancellationToken);

        bindingResult = await _bindings.GetByAsync(x => x.Id == bindingId, cancellationToken);
        if (bindingResult.IsFailure)
            return bindingResult.Error;

        binding = bindingResult.Value;

        await _bindings.RemoveAsync(binding.Id, cancellationToken);
        int remainingAfterRemoval = await _bindings.CountByPlanIdAsync(
            binding.PlanId,
            cancellationToken);

        // Publish для AccessService onboarding-flow auto-sync (issue #68): TELEGRAM step
        // removes только когда RemainingBindingsCount == 0 (последний unbind).
        await _outbox.PublishAsync(new ChatBindingUnboundFromPlan(
            binding.Id, binding.PlanId, binding.TelegramChatId, remainingAfterRemoval));

        UnitResult<Error> saveResult = await _transactions.CommitTransactionAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Chat binding removed. BindingId={BindingId} PlanId={PlanId} ChatId={ChatId} RemovedBy={RemovedBy}",
            binding.Id, binding.PlanId, binding.TelegramChatId, _user.UserId);

        return "ok";
    }
}
