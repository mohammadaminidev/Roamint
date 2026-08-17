# Multi-stage build for the Roamint.Web Blazor Server host.
# See docs/features/0023-oracle-deployment.md.
#
# Built ON the target VM (docker compose build) so the image matches its CPU arch —
# Oracle Always Free is ARM64, Hetzner is x86-64 — with no cross-arch buildx dance.
# The .NET SDK/runtime base images are multi-arch, so this Dockerfile is arch-agnostic.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the whole repo: Central Package Management (Directory.Packages.props), NuGet.config,
# Directory.Build.props and global.json all live at the root and govern restore for every
# referenced project. Enumerating ~15 csproj paths for finer layer caching isn't worth the
# fragility for a single-user deploy — the .dockerignore already keeps the context small.
COPY . .

RUN dotnet restore src/Bootstrap/Roamint.Web/Roamint.Web.csproj
RUN dotnet publish src/Bootstrap/Roamint.Web/Roamint.Web.csproj \
    -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Kestrel binds plain HTTP on 8080; Caddy terminates TLS and reverse-proxies here.
# RUNNING_BEHIND_PROXY disables the app's own http->https redirect (Caddy owns that at the edge).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    RUNNING_BEHIND_PROXY=true \
    Receipts__ImageRoot=/data/receipts

# Receipt images (FileSystemReceiptImageStore) are written to disk under Receipts:ImageRoot.
# Create the dir owned by the image's non-root APP_UID *before* the volume mounts, so the fresh
# named volume inherits that ownership on first mount and the non-root app can write to it.
RUN mkdir -p /data/receipts && chown -R $APP_UID:$APP_UID /data

COPY --from=build /app ./

USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "Roamint.Web.dll"]
