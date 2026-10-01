FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Lantern.Till/Lantern.Till.csproj src/Lantern.Till/
RUN dotnet restore src/Lantern.Till/Lantern.Till.csproj
COPY src/Lantern.Till/ src/Lantern.Till/
RUN dotnet publish src/Lantern.Till/Lantern.Till.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The registration database and Data Protection keys live on a volume the non-root app user owns.
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
ENTRYPOINT ["dotnet", "Lantern.Till.dll"]
