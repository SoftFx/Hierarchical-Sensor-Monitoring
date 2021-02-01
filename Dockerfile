FROM mcr.microsoft.com/dotnet/core/sdk:3.1
RUN apt-get update && apt-get install -y \
	nuget \
	liblmdb0 \
	lmdb-utils \
	liblmdb-dev 
RUN apt-get upgrade
WORKDIR /app
COPY ./Release .
EXPOSE 44330
EXPOSE 22900
ENTRYPOINT ["dotnet", "HSMServer.dll"]