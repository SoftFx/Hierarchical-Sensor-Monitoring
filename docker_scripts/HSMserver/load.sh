#!/bin/bash

Version="${1:-latest}"
BaseDirectory="${2:-/usr/HSM/}"

if [[ -z "$BaseDirectory" ]]; then
    BaseDirectory="/usr/HSM/"
fi

echo "Version:" $Version
echo "Base directory:" $BaseDirectory

Repository="hsmonitoring/hierarchical_sensor_monitoring"
ExpectedImageTag="${Repository}:$Version"
ContainerName="HSMServer"

echo "Load image v.$Version"
docker pull $ExpectedImageTag

echo "Removing all stopped/unused containers"
docker container prune -f

echo "Current running container"
CurrentContainerId=$(docker ps -q -f "name=$ContainerName")
if [[ -n "$CurrentContainerId" ]]; then
    echo "Current id container = $CurrentContainerId"

    echo "Stop container $CurrentContainerId"
    docker stop $CurrentContainerId

    echo "Remove container $CurrentContainerId"
    docker rm $CurrentContainerId
else
    echo "Running container hasn't be found"
fi

ExpectedImageId=$(docker images --filter=reference=$ExpectedImageTag -q)
if [[ -n "$ExpectedImageId" ]]; then
    echo "Image id to run = $ExpectedImageId"

    LogsFolder="$BaseDirectory/Logs:/app/Logs"
    SensorConfigFolder="$BaseDirectory/Config:/app/Config"
    EnvironmentDatabaseFolder="$BaseDirectory/Databases:/app/Databases"
    DatabasesBackupsFolder="$BaseDirectory/DatabasesBackups:/app/DatabasesBackups"

    SensorDataPort="44330:44330"
    SensorSitePort="44333:44333"

    docker run -d -it -u 0 --name "${ContainerName}_$Version" -v "$LogsFolder" -v "$SensorConfigFolder" -v "$EnvironmentDatabaseFolder" -v "$DatabasesBackupsFolder" -p "$SensorDataPort" -p "$SensorSitePort" "$ExpectedImageId"
else
    echo "Expected image hasn't been found"
fi
