using AssignmentReviewService.Core.Features.Installations.UseCases;

namespace AssignmentReviewService.UnitTests.Installations;

public sealed class StartInstallationValidatorTests
{
    [Theory]
    [InlineData("/\\evil.example/path")]
    [InlineData("/safe\r\nX-Injected: value")]
    public async Task Redirect_ambiguous_or_control_characters_should_be_rejected(string returnUrl)
    {
        var sut = new StartInstallationValidator();

        var result = await sut.ValidateAsync(new StartInstallationCommand(returnUrl));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Oversized_redirect_should_be_rejected()
    {
        var sut = new StartInstallationValidator();

        var result = await sut.ValidateAsync(
            new StartInstallationCommand("/" + new string('a', 2048)));

        Assert.False(result.IsValid);
    }
}
