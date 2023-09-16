docker rm ping_vpn_final -f
$containerId = docker run --user 0 -ti -d --cap-add=NET_ADMIN --cap-add=NET_RAW --name ping_vpn_final -e TOKEN=e9f2aba68ae3ecd873215a48d621e555844e71e1931ca2c6f398feba4160ae21 -e TECHNOLOGY=NordLynx redleonfire/vpn_in_dotnet:latest

docker exec -u root -ti -d $containerId sh -c "dotnet HSMPingModule/HSMPingModule.dll"