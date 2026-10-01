FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
# Restore with the source present: the SDK only adds Blazor's framework assets (_framework/blazor.web.js)
# when it can see .razor files at restore time.
COPY src/Lantern.Auth/ src/Lantern.Auth/
COPY src/Lantern.OutletAdmin/ src/Lantern.OutletAdmin/
RUN dotnet restore src/Lantern.OutletAdmin/Lantern.OutletAdmin.csproj
RUN dotnet publish src/Lantern.OutletAdmin/Lantern.OutletAdmin.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Lantern.OutletAdmin.dll"]
