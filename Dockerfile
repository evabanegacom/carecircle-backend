# Build from the PeerCare directory so the project file is at the context root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["PeerCare.csproj", "./"]
RUN dotnet restore "PeerCare.csproj"

COPY . .
RUN dotnet publish "PeerCare.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=10000 \
    Security__DataProtectionKeysPath=/tmp/peercare-dp-keys

EXPOSE 10000

COPY --from=build /app/publish .

# Official .NET 10 images provide the non-root app user and APP_UID.
USER $APP_UID

# Render supplies PORT at runtime; default to 10000 for local container runs.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet PeerCare.dll"]
