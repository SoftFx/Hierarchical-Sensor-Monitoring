Param(
	[string]$Version,
	[string]$BaseDirectory
)

if ([string]::IsNullOrEmpty($Version))
{
	$Version = "latest"
}

if ([string]::IsNullOrEmpty($BaseDirectory))
{
	$BaseDirectory = "/usr/HSM/" # Unix base directory
	# $BaseDirectory = "C:\HSM\" # Windows base directory
}

Write-Host "Version:" $Version
Write-Host "Base directory:" $BaseDirectory

Write-Host $LogsFolder
$Repository = "hsmonitoring/hierarchical_sensor_monitoring"
$ExpectedImageTag = "${Repository}:$Version"
$ContainerName = "HSMServer"

Write-Host "Current running container"
$CurrentContainerId = docker ps -q -f "name=$ContainerName"
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

	$LogsFolder = $BaseDirectory + "Logs:/app/Logs"
	$SensorConfigFolder = $BaseDirectory + "Config:/app/Config"
	$EnvironmentDatabaseFolder = $BaseDirectory + "Databases:/app/Databases"
	$DatabasesBackupsFolder = $BaseDirectory + "DatabasesBackups:/app/DatabasesBackups"

	$SensorDataPort = "44330:44330"
	$SensorSitePort = "44333:44333"

	docker run -d -it -u 0 --name "${ContainerName}_$Version" -v $LogsFolder -v $SensorConfigFolder -v $EnvironmentDatabaseFolder -v $DatabasesBackupsFolder -p $SensorDataPort -p $SensorSitePort $ExpectedImageId
}
else
{
    Write-Host "Expected image hasn't been found"
}