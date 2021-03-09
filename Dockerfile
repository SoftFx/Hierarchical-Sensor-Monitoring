#FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
#COPY . ./
#RUN dotnet restore HSMServer/HSMServer.sln
#RUN dotnet publish HSMServer/HSMServer.sln -c Release --no-restore -o Release

#FROM mcr.microsoft.com/dotnet/core/sdk:3.1-nanoserver-1909 AS win-build
#COPY [".",  "./"]
#RUN ["powershell", "NET USER"]
#RUN ["powershell", "NET USER admin /add"]
#RUN [NET LOCALGROUP Administrators /add admin]
#USER admin
#RUN ["powershell", "dotnet restore", "HSMClient/HSMClient.sln"]
#RUN ["powershell", "dotnet publish", "HSMClient/HSMClient.sln -c Release --no-restore -o Client"]
#RUN runas /noprofile /user:Administrator dotnet restore "HSMClient/HSMClient.sln"
#RUN dotnet publish "HSMClient/HSMClient.sln" -c Release --no-restore -o Client


FROM mcr.microsoft.com/dotnet/core/sdk:3.1
RUN apt-get update && apt-get install -y \
	nuget \
	liblmdb0 \
	lmdb-utils \
	liblmdb-dev 
WORKDIR /app
#ADD /home/runner/work/Hierarchical-Sensor-Monitoring/Hierarchical-Sensor-Monitoring/Release .
#RUN unzip /home/runner/work/Hierarchical-Sensor-Monitoring/Hierarchical-Sensor-Monitoring/Release/Release.zip
COPY ./Release .
EXPOSE 44330 22900
ENTRYPOINT ["dotnet", "HSMServer.dll"]