FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Lantern.Api/Lantern.Api.csproj src/Lantern.Api/
RUN dotnet restore src/Lantern.Api/Lantern.Api.csproj
COPY src/Lantern.Api/ src/Lantern.Api/
RUN dotnet publish src/Lantern.Api/Lantern.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Lantern.Api.dll"]
