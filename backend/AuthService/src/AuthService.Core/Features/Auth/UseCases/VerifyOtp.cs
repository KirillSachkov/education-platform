using AuthService.Core.Database;
using AuthService.Core.Options;
using AuthService.Core.Services;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Features.Auth.UseCases;

public sealed record VerifyOtpRequest(
    string Email,
    string Code,
    bool ConsentOfferAccepted = false,
    bool ConsentPersonalDataAccepted = false,
    bool ConsentMarketingAccepted = false);

/// <summary>
///     Контекст HTTP-вызова, используется для фиксации согласий
///     (юр-доказательство при споре).
/// </summary>
public sealed record ClientContext(string IpAddress, string UserAgent);

public sealed record VerifyOtpCommand(VerifyOtpRequest Request, ClientContext Client) : ICommand;

public sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.Request.Email)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("email"))
            .EmailAddress().WithError(GeneralErrors.ValueIsInvalid("email"));

        RuleFor(x => x.Request.Code)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("code"));
    }
}

public sealed class VerifyOtpEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/otp/verify", async Task<EndpointResult<string>> (
                    [FromBody] VerifyOtpRequest request,
                    [FromServices] VerifyOtpHandler handler,
                    HttpContext httpContext,
                    CancellationToken ct) =>
                await handler.Handle(
                    new VerifyOtpCommand(request, ExtractClientContext(httpContext)),
                    ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("otp");
    }

    private static ClientContext ExtractClientContext(HttpContext httpContext)
    {
        // Reads real client IP after nginx — relies on UseForwardedHeaders
        // в UsePlatformDefaults (PlatformBootstrap). Без него получили бы IP
        // nginx-контейнера, а в auth.user_consents запись с IP — юр-доказательство
        // по 152-ФЗ, поэтому подмена сломает compliance.
        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 500)
            userAgent = userAgent[..500];
        if (string.IsNullOrEmpty(userAgent))
            userAgent = "unknown";
        return new ClientContext(ip, userAgent);
    }
}

public sealed class VerifyOtpHandler : ICommandHandler<string, VerifyOtpCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly SignInManager<Account> _signInManager;
    private readonly IProfileRepository _profileRepository;
    private readonly IConsentRepository _consentRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly IOtpStore _otpStore;
    private readonly UsernameGenerator _usernameGenerator;
    private readonly OtpAttemptLimiter _attemptLimiter;
    private readonly AuthServiceOptions _authOptions;
    private readonly IValidator<VerifyOtpCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public VerifyOtpHandler(
        UserManager<Account> userManager,
        SignInManager<Account> signInManager,
        IProfileRepository profileRepository,
        IConsentRepository consentRepository,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        IOtpStore otpStore,
        UsernameGenerator usernameGenerator,
        OtpAttemptLimiter attemptLimiter,
        IOptions<AuthServiceOptions> authOptions,
        IValidator<VerifyOtpCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _profileRepository = profileRepository;
        _consentRepository = consentRepository;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _otpStore = otpStore;
        _usernameGenerator = usernameGenerator;
        _attemptLimiter = attemptLimiter;
        _authOptions = authOptions.Value;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        VerifyOtpCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        string email = command.Request.Email.Trim().ToLowerInvariant();

        if (!await _attemptLimiter.TryAttemptAsync(email))
        {
            _audit.LogLoginFailed(email, "otp_rate_limited");
            return AuthErrors.TooManyAttempts();
        }

        // Check lockout before consuming OTP — so locked users don't waste their code
        Account? user = await _userManager.FindByEmailAsync(email);
        if (user is not null && await _userManager.IsLockedOutAsync(user))
        {
            _audit.LogLoginFailed(email, "locked");
            return AuthErrors.AccountLocked();
        }

        // Mandatory consent check for new users — BEFORE consuming OTP.
        // Иначе при отсутствии consents мы бы съели одноразовый код и юзер не смог
        // бы повторить регистрацию без запроса нового. Существующих юзеров (login)
        // эта проверка не касается — флаги для них игнорируются.
        if (user is null
            && (!command.Request.ConsentOfferAccepted
                || !command.Request.ConsentPersonalDataAccepted))
        {
            _audit.LogLoginFailed(email, "consent_missing");
            return AuthErrors.MandatoryConsentMissing();
        }

        bool valid = await _otpStore.VerifyAndConsumeAsync(email, command.Request.Code);
        if (!valid)
        {
            _audit.LogLoginFailed(email, "invalid_otp");
            return AuthErrors.InvalidOtpCode();
        }

        if (user is not null)
        {
            // Existing user: just login. Consents were collected at registration;
            // future "accept new version" modal will write fresh consents separately.
            await _userManager.ResetAccessFailedCountAsync(user);
            await _signInManager.SignInAsync(user, isPersistent: true);

            // Update last_login_at for admin observability. Tracked entity — SaveChanges flushes it.
            user.LastLoginAt = DateTime.UtcNow;

            // Publish UserLoggedIn — login-audit / session-lifecycle event для consumers.
            await _outboxService.PublishAsync(new UserLoggedIn(user.Id, user.UserName));
            UnitResult<Error> publishResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (publishResult.IsFailure)
                return publishResult.Error;

            _audit.LogLoginSuccess(user.Id, "otp");
            return "authenticated";
        }

        string baseUsername = email.Split('@')[0];
        string username = await _usernameGenerator.GenerateUniqueAsync(baseUsername);

        var newUser = new Account
        {
            Id = Guid.CreateVersion7(),
            UserName = username,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        IdentityResult createResult = await _userManager.CreateAsync(newUser);
        if (!createResult.Succeeded)
            return AuthErrors.FromIdentityError(createResult.Errors.First());

        IdentityResult roleResult = await _userManager.AddToRoleAsync(newUser, _authOptions.DefaultRole);
        if (!roleResult.Succeeded)
            return AuthErrors.FromIdentityError(roleResult.Errors.First());

        await _profileRepository.EnsureExistsAsync(newUser.Id, cancellationToken);

        // Persist consents (юр-доказательство — IP/UA/version фиксируются).
        // Возрастной чекбокс отдельно НЕ собираем: возрастной ценз 14+ зафиксирован
        // в самой оферте (раздел 1), акцепт OFFER = подтверждение возраста.
        DateTime now = DateTime.UtcNow;
        await PersistConsentAsync(newUser.Id, ConsentType.OFFER, LegalDocumentVersions.Offer, command.Client, now, cancellationToken);
        await PersistConsentAsync(newUser.Id, ConsentType.PERSONAL_DATA, LegalDocumentVersions.PersonalDataConsent, command.Client, now, cancellationToken);
        if (command.Request.ConsentMarketingAccepted)
        {
            await PersistConsentAsync(newUser.Id, ConsentType.MARKETING, LegalDocumentVersions.MarketingConsent, command.Client, now, cancellationToken);
        }

        await _outboxService.PublishAsync(new UserCreated(newUser.Id, newUser.UserName));

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        _audit.LogRegistration(newUser.Id, "otp");

        await _signInManager.SignInAsync(newUser, isPersistent: true);

        _audit.LogLoginSuccess(newUser.Id, "otp");
        return "authenticated";
    }

    private async Task PersistConsentAsync(
        Guid userId,
        ConsentType type,
        string version,
        ClientContext client,
        DateTime now,
        CancellationToken ct)
    {
        var consent = new UserConsent
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ConsentType = type,
            DocumentVersion = version,
            AcceptedAt = now,
            IpAddress = client.IpAddress,
            UserAgent = client.UserAgent,
        };
        await _consentRepository.AddAsync(consent, ct);
    }
}
