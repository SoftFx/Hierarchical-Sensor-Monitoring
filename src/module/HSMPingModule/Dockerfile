FROM ghcr.io/bubuntux/nordvpn

RUN apt-get update
RUN apt-get install sudo
RUN apt-get install -y dotnet-sdk-8.0

COPY . /HSMPingModuleSrc

WORKDIR /HSMPingModuleSrc
RUN dotnet publish -c Release --os linux --arch x64 -o /HSMPingModule

WORKDIR /
RUN rm -rf /HSMPingModuleSrc