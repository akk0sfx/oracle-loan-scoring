# Multi-stage build: the SDK image compiles, the much smaller ASP.NET runtime image runs.
# Build context: the scoring-api/ folder (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, copying only project files: this layer is cached until dependencies change.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ScoringApi/ScoringApi.csproj src/ScoringApi/
COPY src/ScoringApi.Core/ScoringApi.Core.csproj src/ScoringApi.Core/
RUN dotnet restore src/ScoringApi/ScoringApi.csproj

COPY src/ src/
RUN dotnet publish src/ScoringApi/ScoringApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# .NET 8+ images ship a non-root "app" user; APP_UID holds its numeric id.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ScoringApi.dll"]
