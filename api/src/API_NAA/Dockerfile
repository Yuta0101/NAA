FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/API_NAA/API_NAA.csproj", "src/API_NAA/"]
COPY . .
WORKDIR "/src/src/API_NAA"

RUN --mount=type=secret,id=source_url \
    --mount=type=secret,id=source_cred \
    source_url=$(cat /run/secrets/source_url) && \
    source_userpass=$(cat /run/secrets/source_cred) && \
    dotnet nuget add source "$source_url" \
      --name nuget-repo \
      --username "${source_userpass%%:*}" \
      --password "${source_userpass#*:}" \
      --store-password-in-clear-text && \
    dotnet restore "./API_NAA.csproj" && \
    dotnet nuget remove source nuget-repo
RUN dotnet build "./API_NAA.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./API_NAA.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "API_NAA.dll"]
