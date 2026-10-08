using Core.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;

namespace TelegramBotService.Contracts.HttpCommunication;

internal sealed class TelegramBotServiceClient : BaseHttpClient, ITelegramBotServiceClient
{
    private const string SERVICE_NAME = "TelegramBotService";

    public TelegramBotServiceClient(
        HttpClient httpClient,
        ILogger<TelegramBotServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public async Task<Result<bool, Error>> HasActiveChatBindingAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        Result<HasActiveChatBindingResponse, Error> result =
            await GetAsync<HasActiveChatBindingResponse>(
                $"/internal/telegram/plans/{planId}/has-active-chat-binding/",
                cancellationToken);

        return result.IsSuccess
            ? Result.Success<bool, Error>(result.Value.HasActive)
            : Result.Failure<bool, Error>(result.Error);
    }

    public Task<Result<PlanMembershipDto, Error>> CheckPlanMembershipAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        return GetAsync<PlanMembershipDto>(
            $"/internal/telegram/users/{userId}/plans/{planId}/membership/",
            cancellationToken);
    }
}
