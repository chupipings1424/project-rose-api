# syntax=docker/dockerfile:1
# Project ROSE in one container: the React UI (built from the project-rose repo on GitHub)
# served by the .NET API. Render builds this on every push to main.

# ---- 1. The React UI ---------------------------------------------------------
FROM node:24-alpine AS ui
ARG UI_REPO=chupipings1424/project-rose
ARG UI_REF=main
WORKDIR /ui
# A remote ADD is re-checked on every build, so a new UI commit busts the cache from here on.
ADD https://github.com/${UI_REPO}/archive/${UI_REF}.tar.gz /tmp/ui.tar.gz
RUN tar -xzf /tmp/ui.tar.gz --strip-components=1 \
 && npm ci --no-audit --no-fund \
 && npm run build

# ---- 2. The .NET API -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api
WORKDIR /src
# Project files first: restore is cached until a dependency changes.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Rose.Domain/Rose.Domain.csproj src/Rose.Domain/
COPY src/Rose.Application/Rose.Application.csproj src/Rose.Application/
COPY src/Rose.Infrastructure/Rose.Infrastructure.csproj src/Rose.Infrastructure/
COPY src/Rose.Api/Rose.Api.csproj src/Rose.Api/
RUN dotnet restore src/Rose.Api/Rose.Api.csproj
COPY src/ src/
RUN dotnet publish src/Rose.Api/Rose.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# ---- 3. What actually runs -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=ui /ui/dist ./wwwroot
# Reminders and habits run on local time. Render sits behind a proxy (https -> http).
# Workstation GC keeps memory low on the 512 MB free plan.
ENV TZ=Asia/Manila \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER $APP_UID
EXPOSE 8080
# Render tells the app which port to use through $PORT.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet Rose.Api.dll"]