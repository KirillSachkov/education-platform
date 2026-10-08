# syntax=docker/dockerfile:1.7
ARG DOTNET_SDK_VERSION=10.0.203
ARG DOTNET_RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src

ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
    NUGET_XMLDOC_MODE=skip

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet tool install --global dotnet-ef --version "10.0.*"
ENV PATH="/root/.dotnet/tools:${PATH}"

# ── Project files only (restore cache layer) ──
COPY [".editorconfig", "."]
COPY [".globalconfig", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["BannedSymbols.txt", "."]

COPY ["Shared/Directory.Build.props", "Shared/"]
COPY ["Shared/Authentication/Authentication.csproj", "Shared/Authentication/"]
COPY ["Shared/Common/Common.csproj", "Shared/Common/"]
COPY ["Shared/ContentAccess/ContentAccess/ContentAccess.csproj", "Shared/ContentAccess/ContentAccess/"]
COPY ["Shared/DomainEvents/DomainEvents.csproj", "Shared/DomainEvents/"]
COPY ["Shared/Database/Database.csproj", "Shared/Database/"]
COPY ["Shared/Messaging/RabbitMqMessaging/RabbitMqMessaging.csproj", "Shared/Messaging/RabbitMqMessaging/"]
COPY ["Shared/Observability/Observability.csproj", "Shared/Observability/"]
COPY ["Shared/PlatformBootstrap/PlatformBootstrap.csproj", "Shared/PlatformBootstrap/"]
COPY ["Shared/SharedKernel/SharedKernel.csproj", "Shared/SharedKernel/"]
COPY ["Shared/Core/Core.csproj", "Shared/Core/"]
COPY ["Shared/Framework/Framework.csproj", "Shared/Framework/"]
COPY ["Shared/AI/AI/AI.csproj", "Shared/AI/AI/"]
COPY ["Shared/AI/AI.OpenAiCompatible/AI.OpenAiCompatible.csproj", "Shared/AI/AI.OpenAiCompatible/"]

COPY ["FileService/Directory.Build.props", "FileService/"]
COPY ["FileService/src/FileService.Contracts/FileService.Contracts.csproj", "FileService/src/FileService.Contracts/"]

COPY ["EducationContentService/Directory.Build.props", "EducationContentService/"]
COPY ["EducationContentService/src/EducationContentService.Contracts/EducationContentService.Contracts.csproj", "EducationContentService/src/EducationContentService.Contracts/"]

COPY ["MaterialProcessingService/Directory.Build.props", "MaterialProcessingService/"]
COPY ["MaterialProcessingService/src/MaterialProcessingService.Contracts/MaterialProcessingService.Contracts.csproj", "MaterialProcessingService/src/MaterialProcessingService.Contracts/"]
COPY ["MaterialProcessingService/src/MaterialProcessingService.Core/MaterialProcessingService.Core.csproj", "MaterialProcessingService/src/MaterialProcessingService.Core/"]
COPY ["MaterialProcessingService/src/MaterialProcessingService.Domain/MaterialProcessingService.Domain.csproj", "MaterialProcessingService/src/MaterialProcessingService.Domain/"]
COPY ["MaterialProcessingService/src/MaterialProcessingService.Infrastructure.FFmpeg/MaterialProcessingService.Infrastructure.FFmpeg.csproj", "MaterialProcessingService/src/MaterialProcessingService.Infrastructure.FFmpeg/"]
COPY ["MaterialProcessingService/src/MaterialProcessingService.Infrastructure.AI/MaterialProcessingService.Infrastructure.AI.csproj", "MaterialProcessingService/src/MaterialProcessingService.Infrastructure.AI/"]
COPY ["MaterialProcessingService/src/MaterialProcessingService.Infrastructure.Postgres/MaterialProcessingService.Infrastructure.Postgres.csproj", "MaterialProcessingService/src/MaterialProcessingService.Infrastructure.Postgres/"]
COPY ["MaterialProcessingService/src/MaterialProcessingService.Web/MaterialProcessingService.Web.csproj", "MaterialProcessingService/src/MaterialProcessingService.Web/"]

COPY ["docker/healthcheck/healthcheck.csproj", "docker/healthcheck/"]

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "MaterialProcessingService/src/MaterialProcessingService.Web/MaterialProcessingService.Web.csproj" && \
    dotnet restore "docker/healthcheck/healthcheck.csproj"

# ── Full source ──
COPY ["Shared/", "Shared/"]
COPY ["FileService/", "FileService/"]
COPY ["EducationContentService/", "EducationContentService/"]
COPY ["MaterialProcessingService/", "MaterialProcessingService/"]
COPY ["docker/healthcheck/", "docker/healthcheck/"]

WORKDIR "/src/MaterialProcessingService/src/MaterialProcessingService.Web"

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "MaterialProcessingService.Web.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "/src/docker/healthcheck/healthcheck.csproj" \
    -c Release -o /app/healthcheck --no-restore /p:UseAppHost=false

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet ef migrations bundle \
    --project "../MaterialProcessingService.Infrastructure.Postgres/MaterialProcessingService.Infrastructure.Postgres.csproj" \
    --startup-project "MaterialProcessingService.Web.csproj" \
    --configuration Release \
    --no-build \
    --force \
    --output /app/efbundle

# Preserve licenses from the packages restored for this application.
COPY ["docker/collect-dependency-notices.sh", "/tmp/collect-dependency-notices.sh"]
RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    sh /tmp/collect-dependency-notices.sh

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final
WORKDIR /app

# GitHub publishing supplies the canonical notices; legacy/local builds may omit it.
RUN --mount=type=secret,id=image_notices,target=/tmp/image-notices.tar \
    if [ -f /tmp/image-notices.tar ]; then \
      mkdir -p /app/licenses && tar -xf /tmp/image-notices.tar -C /app/licenses && \
      cp /app/licenses/THIRD_PARTY_LICENSES/SkiaSharp*.txt /app/licenses/; \
    fi
COPY --link --from=build /app/dependency-notices/ /app/licenses/

# ffmpeg for audio extraction from Kinescope MP4 sources
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

ARG BUILD_REVISION=unknown
ARG BUILD_VERSION=dev
LABEL org.opencontainers.image.source="https://github.com/KirillSachkov/education-platform" \
      org.opencontainers.image.revision="${BUILD_REVISION}" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.title="MaterialProcessingService"

COPY --link --from=build /app/publish .
COPY --link --from=build /app/efbundle .
COPY --link --from=build /app/healthcheck ./healthcheck/
RUN chmod +x efbundle

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD ["dotnet", "healthcheck/healthcheck.dll", "8011"]

USER app

EXPOSE 8011

ENTRYPOINT ["dotnet", "MaterialProcessingService.Web.dll"]
