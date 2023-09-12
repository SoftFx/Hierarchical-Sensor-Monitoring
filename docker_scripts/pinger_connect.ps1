$PingVersion = $args[0]
$HsmVersion = $args[1]
$PingContainerName = "HSMPingModule_$PingVersion"
$HSMContainerName = "HSMServer_$HsmVersion"
$ConnectionNetworkName = "HSM-Ping-network"


docker network disconnect $ConnectionNetworkName $PingContainerName
docker network disconnect $ConnectionNetworkName $HSMContainerName
docker network rm $ConnectionNetworkName

docker network create -d bridge $ConnectionNetworkName

docker network connect $ConnectionNetworkName "HSMServer_$HsmVersion" --alias

docker network connect $ConnectionNetworkName "HSMPingModule_$PingVersion"