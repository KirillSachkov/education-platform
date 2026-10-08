using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.InviteLinks;

/// <summary>
/// Pure domain tests for <see cref="InviteLink"/> + <see cref="InviteToken"/>.
/// No DB / HTTP — only factory + redeem-validation rules.
/// </summary>
public class InviteLinkFactoryTests
{
    [Fact]
    public void Generate_token_yields_unique_22_char_strings()
    {
        InviteToken t1 = InviteToken.Generate();
        InviteToken t2 = InviteToken.Generate();

        Assert.Equal(22, t1.Value.Length);
        Assert.NotEqual(t1.Value, t2.Value);
    }

    [Fact]
    public void Validate_for_redeem_passes_when_active_and_unexpired()
    {
        InviteLink link = InviteLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            multiUse: true,
            maxUses: null,
            expiresAt: null,
            label: null);

        UnitResult<Error> result = link.ValidateForRedeem(DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_for_redeem_blocks_when_revoked()
    {
        InviteLink link = InviteLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            multiUse: true,
            maxUses: null,
            expiresAt: null,
            label: null);

        link.Revoke();

        UnitResult<Error> result = link.ValidateForRedeem(DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("invite.revoked", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Single_use_link_blocks_second_redeem()
    {
        InviteLink link = InviteLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            multiUse: false,
            maxUses: null,
            expiresAt: null,
            label: null);

        link.RegisterUsage();

        UnitResult<Error> result = link.ValidateForRedeem(DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("invite.usage.exhausted", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Max_uses_blocks_after_limit()
    {
        InviteLink link = InviteLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            multiUse: true,
            maxUses: 2,
            expiresAt: null,
            label: null);

        link.RegisterUsage();
        link.RegisterUsage();

        UnitResult<Error> result = link.ValidateForRedeem(DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("invite.usage.exhausted", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Expired_link_blocks()
    {
        InviteLink link = InviteLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            multiUse: true,
            maxUses: null,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            label: null);

        UnitResult<Error> result = link.ValidateForRedeem(DateTimeOffset.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("invite.expired", result.Error.Messages[0].Code);
    }
}
