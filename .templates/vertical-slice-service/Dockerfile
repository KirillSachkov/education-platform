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
COPY ["Shared/DomainEvents/DomainEvents.csproj", "Shared/DomainEvents/"]
COPY ["Shared/Database/Database.csproj", "Shared/Database/"]
COPY ["Shared/Messaging/RabbitMqMessaging/RabbitMqMessaging.csproj", "Shared/Messaging/RabbitMqMessaging/"]
COPY ["Shared/Observability/Observability.csproj", "Shared/Observability/"]
COPY ["Shared/PlatformBootstrap/PlatformBootstrap.csproj", "Shared/PlatformBootstrap/"]
COPY ["Shared/SharedKernel/SharedKernel.csproj", "Shared/SharedKernel/"]
COPY ["Shared/Core/Core.csproj", "Shared/Core/"]
COPY ["Shared/Framework/Framework.csproj", "Shared/Framework/"]

COPY ["ServiceName/Directory.Build.props", "ServiceName/"]
COPY ["ServiceName/src/ServiceName.Contracts/ServiceName.Contracts.csproj", "ServiceName/src/ServiceName.Contracts/"]
COPY ["ServiceName/src/ServiceName.Core/ServiceName.Core.csproj", "ServiceName/src/ServiceName.Core/"]
COPY ["ServiceName/src/ServiceName.Domain/ServiceName.Domain.csproj", "ServiceName/src/ServiceName.Domain/"]
COPY ["ServiceName/src/ServiceName.Infrastructure.Postgres/ServiceName.Infrastructure.Postgres.csproj", "ServiceName/src/ServiceName.Infrastructure.Postgres/"]
COPY ["ServiceName/src/ServiceName.Web/ServiceName.Web.csproj", "ServiceName/src/ServiceName.Web/"]

COPY ["docker/healthcheck/healthcheck.csproj", "docker/healthcheck/"]

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "ServiceName/src/ServiceName.Web/ServiceName.Web.csproj" && \
    dotnet restore "docker/healthcheck/healthcheck.csproj"

# ── Full source ──
COPY ["Shared/", "Shared/"]
COPY ["ServiceName/", "ServiceName/"]
COPY ["docker/healthcheck/", "docker/healthcheck/"]

WORKDIR "/src/ServiceName/src/ServiceName.Web"

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "ServiceName.Web.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

RUN --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "/src/docker/healthcheck/healthcheck.csproj" \
    -c Release -o /app/healthcheck --no-restore /p:UseAppHost=false

RUN --mount=type=secret,id=nuget_config,target=/src/nuget.config \
    --mount=type=cache,id=nuget-global,target=/root/.nuget/packages,sharing=locked \
    dotnet ef migrations bundle \
    --project "../ServiceName.Infrastructure.Postgres/ServiceName.Infrastructure.Postgres.csproj" \
    --startup-project "ServiceName.Web.csproj" \
    --configuration Release --no-build --force \
    --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final
WORKDIR /app

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
      org.opencontainers.image.title="ServiceName"

COPY --link --from=build /app/publish .
COPY --link --chmod=0755 --from=build /app/efbundle .
COPY --link --from=build /app/healthcheck ./healthcheck/

ENV ASPNETCORE_HTTP_PORTS=__SERVICE_PORT__

USER app

EXPOSE __SERVICE_PORT__

ENTRYPOINT ["dotnet", "ServiceName.Web.dll"]
