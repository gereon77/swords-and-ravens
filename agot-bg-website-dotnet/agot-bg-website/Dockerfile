# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
# 8000: the public site (Razor Pages, PublicApi, PlayApi, chat websocket) — publish this port.
# 8001: the private Basic-Auth-only game-server REST API (UsersApi/GamesApi/RoomsApi/
# NotificationsApi) — see appsettings.json's Kestrel:Endpoints:GameServerApi and Program.cs.
# Only bind/publish this on the docker-compose network the game server runs on, never to the host
# or internet.
EXPOSE 8000
EXPOSE 8001


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["agot-bg-website/agot-bg-website.csproj", "agot-bg-website/"]
RUN dotnet restore "./agot-bg-website/agot-bg-website.csproj"
COPY . .
WORKDIR "/src/agot-bg-website"
RUN dotnet build "./agot-bg-website.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./agot-bg-website.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "agot-bg-website.dll"]