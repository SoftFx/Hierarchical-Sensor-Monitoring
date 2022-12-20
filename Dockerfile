FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app

COPY . ./
RUN dotnet publish src/server/HSMServer/HSMServer.csproj -c Release -o ImageFolder

FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build-env /app/ImageFolder .
EXPOSE 44330 44333
ENTRYPOINT ["dotnet", "HSMServer.dll"]