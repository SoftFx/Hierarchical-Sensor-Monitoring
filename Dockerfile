FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
COPY . ./
RUN dotnet restore src/HSMServer/HSMServer.sln
RUN dotnet publish src/HSMServer/HSMServer.sln -c Release --no-restore -o Release

FROM mcr.microsoft.com/dotnet/core/sdk:3.1
RUN apt-get update && apt-get install -y \
	nuget \
	liblmdb0 \
	lmdb-utils \
	liblmdb-dev 
WORKDIR /app
COPY --from=build-env ./Release .
EXPOSE 44330 22900
ENTRYPOINT ["dotnet", "HSMServer.dll"]