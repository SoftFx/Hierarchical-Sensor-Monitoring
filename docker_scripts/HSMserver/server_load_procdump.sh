#!/bin/bash

VERSION="${1:-latest}"
BASE_DIRECTORY="${2:-/usr/HSM/}"

echo "Version: $VERSION"
echo "Base directory: $BASE_DIRECTORY"

REPOSITORY="hsmonitoring/hierarchical_sensor_monitoring"
EXPECTED_IMAGE_TAG="${REPOSITORY}:${VERSION}"
CONTAINER_NAME="HSMServer"

start_process_monitoring() {
    local container_id="$1"
    local process_pid=1
    local timestamp=$(date +%Y%m%d_%H%M%S)
    local dump_file="Logs/mem_${timestamp}.dmp"

    echo "Starting process monitoring for PID $process_pid in container $container_id"
    echo "Dump file will be saved to: $dump_file"

    docker exec -d "$container_id" bash -c "procdump 1 -e -f OutOfMemoryException -o $dump_file"

    sleep 2

    local procdump_process=$(docker exec "$container_id" bash -c "ps aux | grep -v grep | grep procdump")
    
	#echo "Procdump process: $dump_file: $procdump_process"
	
    if [ -n "$procdump_process" ]; then
        local pid=$(echo "$procdump_process" | awk '{print $2}')
        echo "Procdump successfully started with PID: $pid"
        return 0
    else
        echo "ERROR: Failed to start procdump monitoring"
        return 1
    fi
}

echo "Load image v.$VERSION"
docker pull "$EXPECTED_IMAGE_TAG"

echo "Removing all stopped/unused containers"
docker container prune -f

echo "Checking current running container"
CURRENT_CONTAINER_ID=$(docker ps -q -f "name=$CONTAINER_NAME")

if [ -n "$CURRENT_CONTAINER_ID" ]; then
    echo "Current container ID = $CURRENT_CONTAINER_ID"

    echo "Stopping container $CURRENT_CONTAINER_ID"
    docker stop "$CURRENT_CONTAINER_ID"

    echo "Removing container $CURRENT_CONTAINER_ID"
    docker rm "$CURRENT_CONTAINER_ID"
else
    echo "No running container found"
fi

EXPECTED_IMAGE_ID=$(docker images --filter=reference="$EXPECTED_IMAGE_TAG" -q)

if [ -n "$EXPECTED_IMAGE_ID" ]; then
    echo "Image ID to run = $EXPECTED_IMAGE_ID"

    LOGS_FOLDER="${BASE_DIRECTORY}Logs:/app/Logs"
    SENSOR_CONFIG_FOLDER="${BASE_DIRECTORY}Config:/app/Config"
    ENVIRONMENT_DATABASE_FOLDER="${BASE_DIRECTORY}Databases:/app/Databases"
    DATABASES_BACKUPS_FOLDER="${BASE_DIRECTORY}DatabasesBackups:/app/DatabasesBackups"

    SENSOR_DATA_PORT="44330:44330"
    SENSOR_SITE_PORT="44333:44333"

    NEW_CONTAINER_ID=$(docker run -d -it -u 0 --name "${CONTAINER_NAME}_${VERSION}" \
        -v "$LOGS_FOLDER" \
        -v "$SENSOR_CONFIG_FOLDER" \
        -v "$ENVIRONMENT_DATABASE_FOLDER" \
        -v "$DATABASES_BACKUPS_FOLDER" \
        -p "$SENSOR_DATA_PORT" \
        -p "$SENSOR_SITE_PORT" \
        "$EXPECTED_IMAGE_ID")

    echo "Container started with ID: $NEW_CONTAINER_ID"

	start_process_monitoring "$NEW_CONTAINER_ID"

else
    echo "Expected image not found"
fi