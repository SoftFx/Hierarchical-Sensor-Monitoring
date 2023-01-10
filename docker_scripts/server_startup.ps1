$Version = $args[0]
$Repository = "hsmonitoring/hierarchical_sensor_monitoring"
$ExpectedImageTag = "${Repository}:$Version"

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
    Write-Host "Running images haven't been found"
}

$ExpectedImageId = docker images --filter=reference=$ExpectedImageTag -q
if ($ExpectedImageId)
{
	Write-Host "Image id to run = $ExpectedImageId"

	$LogsFolder = "/usr/HSM/Logs:/app/Logs"
	$SensorDataFolder = "/usr/HSM/MonitoringData:/app/MonitoringData"
	$SensorConfigFolder = "/usr/HSM/Config:/app/Config"
	$EnviromentDatabaseFolder = "/usr/HSM/Databases:/app/Databases"

	$SensorDataPort = "44330:44330"
	$SensorSitePort = "44333:44333"

	docker run -d -it -p $SensorDataPort -p $SensorSitePort -v $LogsFolder -v $SensorDataFolder -v $SensorConfigFolder -v $EnviromentDatabaseFolder $ExpectedImageId
}
else
{
    Write-Host "Expected image hasn't been found"
}
