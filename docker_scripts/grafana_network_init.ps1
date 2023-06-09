$Version = $args[0]
$GrafanaContainerName = "grafana_container"
$ConnectionNetworkName = "HSM-grafana-network"

docker rm -f $GrafanaContainerName
docker network rm $ConnectionNetworkName

docker run -d --user 0 --name $GrafanaContainerName -p 3000:3000 -v /usr/HSM/grafana/data:/var/lib/grafana -v /usr/HSM/grafana/custom.ini:/etc/grafana/grafana.ini -v /usr/HSM/grafana:/cert grafana/grafana-enterprise

docker network create $ConnectionNetworkName

docker network connect $ConnectionNetworkName "HSMServer_$Version" --alias hsm.dev.soft-fx.eu

docker network connect $ConnectionNetworkName $GrafanaContainerName