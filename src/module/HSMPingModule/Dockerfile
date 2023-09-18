FROM ghcr.io/bubuntux/nordvpn

RUN apt-get update
RUN apt-get install sudo
RUN apt-get install -y dotnet-sdk-7.0

COPY . /HSMPingModuleSrc

WORKDIR /HSMPingModuleSrc
RUN dotnet nuget add source /HSMPingModuleSrc/nugets/
RUN dotnet publish -c Release --os linux --arch x64 -o /HSMPingModule

WORKDIR /
RUN rm -rf /HSMPingModuleSrc

# WORKDIR /HSMPingModule
# COPY HSMPingModule/*.csproj .
# COPY HSMPingModule/nugets/ .
# RUN dotnet nuget add source /HSMPingModule/
# RUN dotnet restore
# COPY HSMPingModule .
# RUN dotnet publish -c Release -o /publish

# WORKDIR /publish

# ENTRYPOINT ["dotnet", "HSMPingModule.dll"]