using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Certificates;

namespace ProgressService.Core.Features.Certificates.Queries;

public sealed record GetMyCertificatesQuery : IQuery;

public sealed class GetMyCertificatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/certificates/my",
                async Task<EndpointResult<IReadOnlyList<CourseCertificateResponse>>> (
                    [FromServices] GetMyCertificatesHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyCertificatesQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Сертификаты текущего пользователя, новые первыми. Own-data — читаются только
///     снапшоты, Tier-3 entitlement-чек не нужен. Issue #467.
/// </summary>
public sealed class GetMyCertificatesHandler
    : IQueryHandlerWithResult<IReadOnlyList<CourseCertificateResponse>, GetMyCertificatesQuery>
{
    private readonly ICourseCertificateRepository _certificateRepository;
    private readonly UserScopedData _user;

    public GetMyCertificatesHandler(
        ICourseCertificateRepository certificateRepository,
        UserScopedData user)
    {
        _certificateRepository = certificateRepository;
        _user = user;
    }

    public async Task<Result<IReadOnlyList<CourseCertificateResponse>, Error>> Handle(
        GetMyCertificatesQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = _user.UserId;
        IReadOnlyList<CourseCertificate> certificates = await _certificateRepository.GetManyByAsync(
            c => c.UserId == userId,
            cancellationToken);

        IReadOnlyList<CourseCertificateResponse> items = certificates
            .OrderByDescending(c => c.IssuedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new CourseCertificateResponse(
                c.Id,
                c.SerialNumber,
                c.HolderName,
                c.CourseTitle,
                c.CourseId,
                c.IssuedAt))
            .ToList();

        return Result.Success<IReadOnlyList<CourseCertificateResponse>, Error>(items);
    }
}
