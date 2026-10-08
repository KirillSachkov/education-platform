# syntax=docker/dockerfile:1.7
# Re-enabled in prod deploy (v1.1.0).
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
COPY ["Shared/Database/Database.csproj", "Shared/Database/"]
COPY ["Shared/ContentAccess/ContentAccess/ContentAccess.csproj", "Shared/ContentAccess/ContentAccess/"]
COPY ["Shared/Email/Email.csproj", "Shared/Email/"]
COPY ["Shared/Messaging/RabbitMqMessaging/RabbitMqMessaging.csproj", "Shared/Messaging/RabbitMqMessaging/"]
COPY ["Shared/Navigation/Navigation.csproj", "Shared/Navigation/"]
COPY ["Shared/Observability/Observability.csproj", "Shared/Observability/"]
COPY ["Shared/PlatformBootstrap/PlatformBootstrap.csproj", "Shared/PlatformBootstrap/"]
COPY ["Shared/SharedKernel/SharedKernel.csproj", "Shared/SharedKernel/"]
COPY ["Shared/Core/Core.csproj", "Shared/Core/"]
COPY ["Shared/Framework/Framework.csproj", "Shared/Framework/"]

# Cross-service Contracts ProjectReferences (NotificationService.Core).
COPY ["AccessService/Directory.Build.props", "AccessService/"]
COPY ["AccessService/src/AccessService.Contracts/AccessService.Contracts.csproj", "AccessService/src/AccessService.Contracts/"]
COPY ["AuthService/Directory.Build.props", "AuthService/"]
COPY ["AuthService/src/AuthService.Contracts/AuthService.Contracts.csproj", "AuthService/src/AuthService.Contracts/"]
COPY ["EducationContentService/Directory.Build.props", "EducationContentService/"]
COPY ["EducationContentService/src/EducationContentService.Contracts/EducationContentService.Contracts.csproj", "EducationContentService/src/EducationContentService.Contracts/"]

COPY ["NotificationService/Directory.Build.props", "NotificationService/"]
COPY ["NotificationService/src/NotificationService.Contracts/NotificationService.Contracts.csproj", "NotificationService/src/NotificationService.Contracts/"]
COPY ["NotificationService/src/NotificationService.Core/NotificationService.Core.csproj", "NotificationService/src/NotificationService.Core/"]
COPY ["NotificationService/src/NotificationService.Domain/NotificationService.Domain.csproj", "NotificationService/src/NotificationService.Domain/"]
COPY ["NotificationService/src/NotificationService.Infrastructure.Postgres/NotificationService.Infrastructure.Postgres.csproj", "NotificationService/src/NotificationService.Infrastructure.Postgres/"]
COPY ["NotificationService/src/NotificationService.Web/NotificationService.Web.csproj", "NotificationService/src/NotificationService.Web/"]

COPY ["docker/healthcheck/healthcheck.csproj", "docker/healthcheck/"]

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "NotificationService/src/NotificationService.Web/NotificationService.Web.csproj" && \
    dotnet restore "docker/healthcheck/healthcheck.csproj"

# ── Full source ──
COPY ["Shared/", "Shared/"]
COPY ["AccessService/src/AccessService.Contracts/", "AccessService/src/AccessService.Contracts/"]
COPY ["AuthService/src/AuthService.Contracts/", "AuthService/src/AuthService.Contracts/"]
COPY ["EducationContentService/src/EducationContentService.Contracts/", "EducationContentService/src/EducationContentService.Contracts/"]
COPY ["NotificationService/", "NotificationService/"]
COPY ["docker/healthcheck/", "docker/healthcheck/"]

WORKDIR "/src/NotificationService/src/NotificationService.Web"

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "NotificationService.Web.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "/src/docker/healthcheck/healthcheck.csproj" \
    -c Release -o /app/healthcheck --no-restore /p:UseAppHost=false

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet ef migrations bundle \
    --project "../NotificationService.Infrastructure.Postgres/NotificationService.Infrastructure.Postgres.csproj" \
    --startup-project "NotificationService.Web.csproj" \
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
# даже если auth=password. Без libgssapi_krb5.so.2 в slim-image идёт error в
# stderr на старте каждого .NET сервиса. Мы Kerberos не используем — это noise,
# но грязнит логи и пугает диагностику. ~250KB library, копеечная цена тишины.
# Retry loop guards transient archive.ubuntu.com flakes — one fail blocks the
# entire deploy (build jobs are fan-out, deploy waits for all builds).
# Acquire::Retries handles per-file retries; outer loop catches full-update fails.
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
      org.opencontainers.image.title="NotificationService"

COPY --link --from=build /app/publish .
COPY --link --chmod=0755 --from=build /app/efbundle .
COPY --link --from=build /app/healthcheck ./healthcheck/

ENV ASPNETCORE_HTTP_PORTS=8006

USER app

EXPOSE 8006

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "healthcheck/healthcheck.dll", "8006"]

ENTRYPOINT ["dotnet", "NotificationService.Web.dll"]
