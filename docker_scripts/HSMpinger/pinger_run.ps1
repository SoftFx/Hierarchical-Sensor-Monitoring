$Version = $args[0]
$ContainerPrefix = "HSMPingModule"
$Repository = "hsmonitoring/hsmpingmodule"

$ExpectedImageTag = "${Repository}:$Version"
$FullContainerName = "${ContainerPrefix}_$Version"

$NetworkName = "HSM-Ping-network"
$ServerPrefix = "HSMServer_"

$ServerHost = ""
$NordVpnToken = ""

Write-Host "Current running container"
$CurrentContainerId = docker ps -q -f "name=$ContainerPrefix"
if ($CurrentContainerId)
{
    Write-Host "Current id container = $CurrentContainerId"

    Write-Host "Disconnect ping network"
    docker network disconnect $NetworkName $CurrentContainerId

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
if (!$ExpectedImageId)
{
    Write-Host "Expected image hasn't been found"
    exit
}

Write-Host "Image id to run = $ExpectedImageId"

$LogsFolder = "/usr/HSMPingModule/Logs:/app/Logs"
$ConfigFolder = "/usr/HSMPingModule/Config:/app/Config"
$ContainerId = docker run --user 0 -ti -d --name $FullContainerName --cap-add=NET_ADMIN,NET_RAW -e TOKEN=$NordVpnToken -e TECHNOLOGY=NordLynx -v $LogsFolder -v $ConfigFolder $ExpectedImageId

$StartPingerCommand = "dotnet HSMPingModule/HSMPingModule.dll"
docker exec -u root -ti -d $ContainerId sh -c $StartPingerCommand

$ServerContainerId = docker ps -q -f "name=$ServerPrefix"
if (!$ServerContainerId)
{
    Write-Host "Running HSM server container hasn't be found"
    exit
}

docker network create $NetworkName
docker network connect $NetworkName $ServerContainerId --alias $ServerHost
docker network connect $NetworkName $FullContainerName