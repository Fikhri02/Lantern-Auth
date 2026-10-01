FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Lantern.Auth/Lantern.Auth.csproj src/Lantern.Auth/
COPY src/Lantern.BackOffice/Lantern.BackOffice.csproj src/Lantern.BackOffice/
RUN dotnet restore src/Lantern.BackOffice/Lantern.BackOffice.csproj
COPY src/Lantern.Auth/ src/Lantern.Auth/
COPY src/Lantern.BackOffice/ src/Lantern.BackOffice/
RUN dotnet publish src/Lantern.BackOffice/Lantern.BackOffice.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Lantern.BackOffice.dll"]
