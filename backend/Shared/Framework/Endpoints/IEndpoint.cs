using Microsoft.AspNetCore.Routing;

namespace Framework.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}