FROM mcr.microsoft.com/dotnet/core/sdk:3.1
RUN apt-get update && apt-get install -y \
	nuget \
	liblmdb0 \
	lmdb-utils \
	liblmdb-dev 
COPY . ./
RUN dotnet restore HSMServer/HSMServer.sln
RUN dotnet build HSMServer/HSMServer.sln -c Release --no-restore -o Release
RUN dotnet restore HSMClient/HSMClient.sln
RUN dotnet build HSMClient/HSMClient.sln -c Release --no-restore -o Release/Client
WORKDIR /app
COPY ./Release .
EXPOSE 44330
EXPOSE 22900
ENTRYPOINT ["dotnet", "HSMServer.dll"]