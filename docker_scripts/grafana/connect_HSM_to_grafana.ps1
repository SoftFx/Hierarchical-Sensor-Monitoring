$Version = $args[0]
$ConnectionNetworkName = "HSM-grafana-network"

docker network connect $ConnectionNetworkName "HSMServer_$Version"