using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Features.Courses.Queries;
using ProgressService.Domain;
using ProgressService.Domain.Certificates;

namespace ProgressService.Core.Features.Certificates.Queries;

public sealed record GetCertificateByIdQuery(Guid CertificateId) : IQuery;

public sealed class GetCertificateByIdQueryValidator : AbstractValidator<GetCertificateByIdQuery>
{
    public GetCertificateByIdQueryValidator()
    {
        RuleFor(x => x.CertificateId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetCertificateByIdQuery.CertificateId)));
    }
}

public sealed class GetCertificateByIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Публичная страница проверки сертификата — анонимный доступ намеренно.
        // Возвращаются только снапшоты самого сертификата (имя/курс/дата/серийник),
        // никакого gated-контента. Rate-limit — общая anonymous-read policy сервиса.
        app.MapGet("/progress/certificates/{certificateId:guid}",
                async Task<EndpointResult<CourseCertificateResponse>> (
                    [FromRoute] Guid certificateId,
                    [FromServices] GetCertificateByIdHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetCertificateByIdQuery(certificateId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

/// <summary>
///     Данные сертификата для публичной страницы проверки (issue #467). Читает только
///     снапшоты из <c>course_certificates</c> — без походов в ECS/Auth, поэтому
///     сертификат остаётся проверяемым после удаления курса или смены имени юзера.
/// </summary>
public sealed class GetCertificateByIdHandler
    : IQueryHandlerWithResult<CourseCertificateResponse, GetCertificateByIdQuery>
{
    private readonly IValidator<GetCertificateByIdQuery> _validator;
    private readonly ICourseCertificateRepository _certificateRepository;

    public GetCertificateByIdHandler(
        IValidator<GetCertificateByIdQuery> validator,
        ICourseCertificateRepository certificateRepository)
    {
        _validator = validator;
        _certificateRepository = certificateRepository;
    }

    public async Task<Result<CourseCertificateResponse, Error>> Handle(
        GetCertificateByIdQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        CourseCertificate? certificate = await _certificateRepository.GetByAsync(
            c => c.Id == query.CertificateId,
            cancellationToken);

        if (certificate is null)
        {
            return ProgressErrors.CertificateNotFound();
        }

        return new CourseCertificateResponse(
            certificate.Id,
            certificate.SerialNumber,
            certificate.HolderName,
            certificate.CourseTitle,
            certificate.CourseId,
            certificate.IssuedAt);
    }
}
