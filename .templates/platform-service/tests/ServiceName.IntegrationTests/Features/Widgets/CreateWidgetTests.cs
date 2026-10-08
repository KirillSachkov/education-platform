using System.Net.Http.Json;
using ServiceName.Contracts;
using ServiceName.Domain.Widgets;
using ServiceName.IntegrationTests.Infrastructure;

namespace ServiceName.IntegrationTests.Features.Widgets;

public sealed class CreateWidgetTests : ServiceNameTestsBase
{
    public CreateWidgetTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateWidget_ValidRequest_Returns201()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync("/widgets", new CreateWidgetRequest("Example widget"));

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task CreateWidget_EmptyName_ReturnsValidationError()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync("/widgets", new CreateWidgetRequest(""));

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public void WidgetName_Create_ReturnsSuccessForValidValue()
    {
        Result<WidgetName, Error> result = WidgetName.Of("ok");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Value);
    }
}
