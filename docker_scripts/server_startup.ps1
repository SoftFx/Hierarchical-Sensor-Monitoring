$Version = $args[0]
$Repository = "hsmonitoring/hierarchical_sensor_monitoring"
$ExpectedImageTag = "${Repository}:$Version"
$ContainerName = "HSMServer"

Write-Host "Current running container"
$CurrentContainerId = docker ps --filer "name=$ContainerName" -q
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

$ExpectedImageId = docker images --filter=reference=$ExpectedImageTag -q
if ($ExpectedImageId)
{
	Write-Host "Image id to run = $ExpectedImageId"

	$LogsFolder = "/usr/HSM/Logs:/app/Logs"
	$SensorConfigFolder = "/usr/HSM/Config:/app/Config"
	$EnvironmentDatabaseFolder = "/usr/HSM/Databases:/app/Databases"

	$SensorDataPort = "44330:44330"
	$SensorSitePort = "44333:44333"

	docker run -d -it --name "${ContainerName}_$Version" -v $LogsFolder -v $SensorConfigFolder -v $EnvironmentDatabaseFolder -p $SensorDataPort -p $SensorSitePort $ExpectedImageId
}
else
{
    Write-Host "Expected image hasn't been found"
}