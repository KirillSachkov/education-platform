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
COPY ["Shared/GitHubApp/GitHubApp.csproj", "Shared/GitHubApp/"]
COPY ["Shared/AI/AI/AI.csproj", "Shared/AI/AI/"]
COPY ["Shared/AI/AI.OpenAiCompatible/AI.OpenAiCompatible.csproj", "Shared/AI/AI.OpenAiCompatible/"]

COPY ["AssignmentReviewService/Directory.Build.props", "AssignmentReviewService/"]
COPY ["AssignmentReviewService/src/AssignmentReviewService.Contracts/AssignmentReviewService.Contracts.csproj", "AssignmentReviewService/src/AssignmentReviewService.Contracts/"]
COPY ["AssignmentReviewService/src/AssignmentReviewService.Core/AssignmentReviewService.Core.csproj", "AssignmentReviewService/src/AssignmentReviewService.Core/"]
COPY ["AssignmentReviewService/src/AssignmentReviewService.Domain/AssignmentReviewService.Domain.csproj", "AssignmentReviewService/src/AssignmentReviewService.Domain/"]
COPY ["AssignmentReviewService/src/AssignmentReviewService.Infrastructure.GitHub/AssignmentReviewService.Infrastructure.GitHub.csproj", "AssignmentReviewService/src/AssignmentReviewService.Infrastructure.GitHub/"]
COPY ["AssignmentReviewService/src/AssignmentReviewService.Infrastructure.Postgres/AssignmentReviewService.Infrastructure.Postgres.csproj", "AssignmentReviewService/src/AssignmentReviewService.Infrastructure.Postgres/"]
COPY ["AssignmentReviewService/src/AssignmentReviewService.Web/AssignmentReviewService.Web.csproj", "AssignmentReviewService/src/AssignmentReviewService.Web/"]

COPY ["AuthService/Directory.Build.props", "AuthService/"]
COPY ["AuthService/src/AuthService.Contracts/AuthService.Contracts.csproj", "AuthService/src/AuthService.Contracts/"]

COPY ["docker/healthcheck/healthcheck.csproj", "docker/healthcheck/"]

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "AssignmentReviewService/src/AssignmentReviewService.Web/AssignmentReviewService.Web.csproj" && \
    dotnet restore "docker/healthcheck/healthcheck.csproj"

# ── Full source ──
COPY ["Shared/", "Shared/"]
COPY ["AssignmentReviewService/", "AssignmentReviewService/"]
COPY ["AuthService/src/AuthService.Contracts/", "AuthService/src/AuthService.Contracts/"]
COPY ["docker/healthcheck/", "docker/healthcheck/"]

WORKDIR "/src/AssignmentReviewService/src/AssignmentReviewService.Web"

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "AssignmentReviewService.Web.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "/src/docker/healthcheck/healthcheck.csproj" \
    -c Release -o /app/healthcheck --no-restore /p:UseAppHost=false

# `dotnet ef migrations bundle` boots the Web host to resolve DbContextOptions.
# Host bootstrap calls AddOpenAiCompatible → AiProvidersOptionsValidator runs at
# startup and fails if no providers configured. Real keys live in env at runtime;
# at build time we feed dummy values so design-time host can build.
ENV AI__DEFAULT=aitunnel \
    AI__PROVIDERS__AITUNNEL__KIND=OpenAiCompatible \
    AI__PROVIDERS__AITUNNEL__BASEURL=https://example.invalid/v1/ \
    AI__PROVIDERS__AITUNNEL__APIKEY=ef-bundle-dummy

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet ef migrations bundle \
    --project "../AssignmentReviewService.Infrastructure.Postgres/AssignmentReviewService.Infrastructure.Postgres.csproj" \
    --startup-project "AssignmentReviewService.Web.csproj" \
    --configuration Release --no-build --force \
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

# libgssapi-krb5-2: Npgsql пытается eager-load'нуть Kerberos GSSAPI на старте
# даже если auth=password. Без библиотеки идёт error в stderr на старте каждого
# .NET сервиса. См. AccessService/Dockerfile для подробностей.
RUN <<EOF
set -eu
echo 'Acquire::Retries "5";' > /etc/apt/apt.conf.d/80-retries
echo 'Acquire::http::Timeout "30";' >> /etc/apt/apt.conf.d/80-retries
for i in 1 2 3; do
  if apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2; then
    rm -rf /var/lib/apt/lists/*
    exit 0
  fi
  echo "apt attempt $i/3 failed, sleeping $((i * 10))s..."
  sleep $((i * 10))
done
echo "apt install failed after 3 attempts"
exit 1
EOF

ARG BUILD_REVISION=unknown
ARG BUILD_VERSION=dev
LABEL org.opencontainers.image.source="https://github.com/KirillSachkov/education-platform" \
      org.opencontainers.image.revision="${BUILD_REVISION}" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.title="AssignmentReviewService"

COPY --link --from=build /app/publish .
COPY --link --chmod=0755 --from=build /app/efbundle .
COPY --link --from=build /app/healthcheck ./healthcheck/

ENV ASPNETCORE_HTTP_PORTS=8012

USER app

EXPOSE 8012

# Probe `/health/live` через standalone CLI (см. backend/Shared/PlatformBootstrap),
# не curl — образ aspnet-runtime не имеет curl. Совпадает с
# `docker-compose.yml` healthcheck — образ-level дублирование для standalone deploys
# (K8s, plain `docker run`).
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["dotnet", "healthcheck/healthcheck.dll", "8012"]

ENTRYPOINT ["dotnet", "AssignmentReviewService.Web.dll"]
