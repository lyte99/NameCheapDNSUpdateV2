# console app, no web server, so the runtime image is enough
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

# Creates a non-root user with an explicit UID and adds permission to access the /app folder
# For more info, please refer to https://aka.ms/vscode-docker-dotnet-configure-containers
RUN adduser -u 5678 --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["NameChapDNSUpdateV2.csproj", "./"]
RUN dotnet restore "NameChapDNSUpdateV2.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "NameChapDNSUpdateV2.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "NameChapDNSUpdateV2.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# environment variables
# domain, hosts and dynamicDNSPassword must be supplied at run time.
# do NOT bake dynamicDNSPassword into the image, pass it via compose or `docker run -e`.
ENV intCheckTimerSEC=300
ENV domain=""
ENV hosts=""

ENTRYPOINT ["dotnet", "NameChapDNSUpdateV2.dll"]
