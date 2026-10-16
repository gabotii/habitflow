# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless project files change).
COPY Directory.Build.props Directory.Packages.props HabitFlow.slnx ./
COPY src/HabitFlow.Domain/HabitFlow.Domain.csproj src/HabitFlow.Domain/
COPY src/HabitFlow.Application/HabitFlow.Application.csproj src/HabitFlow.Application/
COPY src/HabitFlow.Infrastructure/HabitFlow.Infrastructure.csproj src/HabitFlow.Infrastructure/
COPY src/HabitFlow.Api/HabitFlow.Api.csproj src/HabitFlow.Api/
RUN dotnet restore src/HabitFlow.Api/HabitFlow.Api.csproj

COPY src/ src/
RUN dotnet publish src/HabitFlow.Api/HabitFlow.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
# The ASP.NET Core image ships ICU and tzdata, needed for IANA time zones.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DataProtection__KeysPath=/app/keys

COPY --from=build /app/publish .
# Data Protection keys (session cookies survive restarts) live in a volume owned by the non-root user.
RUN mkdir -p /app/keys && chown -R app:app /app/keys

USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD ["dotnet", "HabitFlow.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "HabitFlow.Api.dll"]
