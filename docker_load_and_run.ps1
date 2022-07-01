$Version = $args[0]
$Repository = "hsmonitoring/hierarchical_sensor_monitoring"
$ExpectedImageTag = "${Repository}:$Version"

Write-Host "Expected image tag = v.$Version"

Write-Host "Current running container"
$CurrentContainerId = docker container ls -q

if ($CurrentContainerId)
{
    Write-Host "Current id container = $CurrentContainerId"

    Write-Host "Stop container $CurrentContainerId"
    docker stop $CurrentContainerId

    Write-Host "Remove container $CurrentContainerId"
    docker rm $CurrentContainerId
}
else
{
    Write-Host "Running container hasn't be found"
}

Write-Host "Current running image"
$CurrentIdImage = docker ps -q

if ($CurrentIdImage)
{
    Write-Host "Current id image = $CurrentIdImage"
    Write-Host "Stop image $CurrentIdImage"

    docker stop $CurrentIdImage
}
else
{
    Write-Host "Running images hasn't be found"
}

Write-Host "Find and remove image with the same tag"
docker rmi $(docker images --filter=reference=$ExpectedImageTag -q) -f

Write-Host "Load image v.$Version"
docker pull $ExpectedImageTag

$ExpectedImageId = docker images --filter=reference=$ExpectedImageTag -q
Write-Host "Image id = $ExpectedImageId"

$LogsFolder = "/usr/HSM/Logs:/app/Logs"
$SensorDataFolder = "/usr/HSM/MonitoringData:/app/MonitoringData"
$SensorConfigFolder = "/usr/HSM/Config:/app/Config"
$EnviromentDatabaseFolder = "/usr/HSM/Databases:/app/Databases"

$SensorDataPort = "44330:44330"
$SensorSitePort = "443:44333"

docker run -d -it -v $LogsFolder -v $SensorDataFolder -v $SensorConfigFolder -v $EnviromentDatabaseFolder -p $SensorDataPort -p $SensorSitePort $ExpectedImageId