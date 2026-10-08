using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.Requests;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

public sealed record UpdateChatBindingFlagsCommand(
    Guid BindingId,
    UpdateChatBindingFlagsRequest Request) : ICommand;

public sealed class UpdateChatBindingFlagsValidator : AbstractValidator<UpdateChatBindingFlagsCommand>
{
    public UpdateChatBindingFlagsValidator()
    {
        RuleFor(x => x.BindingId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("bindingId"));
    }
}

public sealed class UpdateChatBindingFlagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/telegram/admin/chat-bindings/{bindingId:guid}/",
                async Task<EndpointResult<ChatBindingDto>> (
                    [FromRoute] Guid bindingId,
                    [FromBody] UpdateChatBindingFlagsRequest request,
                    [FromServices] UpdateChatBindingFlagsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateChatBindingFlagsCommand(bindingId, request), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed class UpdateChatBindingFlagsHandler
    : ICommandHandler<ChatBindingDto, UpdateChatBindingFlagsCommand>
{
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly ITransactionManager _transactions;
    private readonly ClaimAnnouncementService _announcer;
    private readonly IValidator<UpdateChatBindingFlagsCommand> _validator;

    public UpdateChatBindingFlagsHandler(
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        ITransactionManager transactions,
        ClaimAnnouncementService announcer,
        IValidator<UpdateChatBindingFlagsCommand> validator)
    {
        _bindings = bindings;
        _accessClient = accessClient;
        _transactions = transactions;
        _announcer = announcer;
        _validator = validator;
    }

    public async Task<Result<ChatBindingDto, Error>> Handle(
        UpdateChatBindingFlagsCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Guid bindingId = command.BindingId;
        Result<ChatBinding, Error> result = await _bindings.GetByAsync(
            x => x.Id == bindingId, cancellationToken);
        if (result.IsFailure)
            return result.Error;

        ChatBinding binding = result.Value;
        if (command.Request.EnrollmentGrantsMembership)
        {
            Result<PlanTelegramInfoDto, Error> planResult =
                await _accessClient.GetPlanTelegramInfoAsync(binding.PlanId, cancellationToken);
            if (planResult.IsFailure)
                return planResult.Error;
            if (planResult.Value.CanonicalTelegramPlanId != binding.PlanId)
            {
                return Error.Validation(
                    "telegram.chat.plan.canonical_required",
                    "Привязка должна относиться к каноническому Telegram-плану");
            }
            if (planResult.Value.Capabilities?.Contains(
                    TelegramGrantChatAccessResolver.COMMUNITY_ACCESS,
                    StringComparer.Ordinal) != true)
            {
                return Error.Validation(
                    "telegram.chat.plan.community_access_required",
                    "План не предоставляет доступ к Telegram-сообществу");
            }
        }

        // Reverse-claim (MembershipGrantsEnrollment) разрешён и для каналов (#416). EnforceMembership
        // остаётся group-only: для каналов нет ChatMember updates для обычных subscribers.
        bool isChannel = binding.ChatType == Domain.CourseChats.ChatType.CHANNEL;
        binding.UpdateFlags(
            command.Request.EnrollmentGrantsMembership,
            membershipGrantsEnrollment: command.Request.MembershipGrantsEnrollment,
            autoKickOnRevoke: command.Request.AutoKickOnRevoke,
            enforceMembership: !isChannel && command.Request.EnforceMembership);

        // Если автор только что включил reverse-claim — best-effort постим закреплённое
        // claim-объявление (идемпотентно через AnnouncementMessageId). Мутирует binding, persist ниже.
        await _announcer.TryPostAndMarkAsync(binding, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new ChatBindingDto(
            binding.Id,
            binding.PlanId,
            binding.TelegramChatId,
            binding.ChatType.ToString(),
            binding.ChatTitle,
            binding.InviteLink,
            binding.EnrollmentGrantsMembership,
            binding.MembershipGrantsEnrollment,
            binding.AutoKickOnRevoke,
            binding.EnforceMembership,
            binding.CreatedAt,
            binding.AnnouncementMessageId);
    }
}
