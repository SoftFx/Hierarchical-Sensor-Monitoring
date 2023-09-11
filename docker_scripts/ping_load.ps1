$Version = $args[0]
$Repository = "hsmonitoring/hsmpingmodule"
$ExpectedImageTag = "${Repository}:$Version"

Write-Host "Load image v.$Version"
docker pull $ExpectedImageTag

Write-Host "Removing all stopped/unused containers"
docker container prune -f

$ContainerName = "HSMPingModule"

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

	$LogsFolder = "/usr/HSMPingModule/Logs:/app/Logs"
	$ConfigFolder = "/usr/HSMPingModule/Config:/app/Config"

	docker run -d -it --name "${ContainerName}_$Version" -v $LogsFolder -v $ConfigFolder $ExpectedImageId
}
else
{
    Write-Host "Expected image hasn't been found"
}