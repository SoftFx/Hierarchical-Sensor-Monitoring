$Version = $args[0]
$ContainerPrefix = "HSMPingModule"
$Repository = "hsmonitoring/hsmpingmodule"

$ExpectedImageTag = "${Repository}:$Version"
$FullContainerName = "${ContainerPrefix}_$Version"

$NetworkName = "HSM-Ping-network"
$ServerPrefix = "HSMServer_"

$ServerHost = '' # must be defined
$NordVpnToken = '' # must be defined

Write-Host "Check running pinger container"
$CurrentContainerId = docker ps -a -q -f "name=$ContainerPrefix"
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

docker network create $NetworkName

$ExpectedImageId = docker images --filter=reference=$ExpectedImageTag -q
if (!$ExpectedImageId)
{
    Write-Host "Expected image hasn't been found"
    exit
}

Write-Host "Image id to run = $ExpectedImageId"

#Unix directories
# $LogsFolder = "/usr/HSMPingModule/Logs:/HSMPingModule/Logs"
# $ConfigFolder = "/usr/HSMPingModule/Config:/HSMPingModule/Config"

#Windows directories
$LogsFolder = "C:\HSMPinger\Logs:/HSMPingModule/Logs"
$ConfigFolder = "C:\HSMPinger\Config:/HSMPingModule/Config"
$SecondConfig = "C:\HSMPinger\SecondConfig:/Config"

$ContainerId = docker run --user 0 -ti -d --name $FullContainerName  --net=$NetworkName --cap-add=NET_ADMIN --cap-add=NET_RAW -e TOKEN=$NordVpnToken -e TECHNOLOGY=NordLynx -v $LogsFolder -v $ConfigFolder -v $SecondConfig $ExpectedImageId


$ServerContainerId = docker ps -q -f "name=$ServerPrefix"
if ($ServerContainerId)
{
    docker network connect $NetworkName $ServerContainerId --alias $ServerHost
}
else
{
    Write-Host "Running HSM server container hasn't be found"
}

Start-Sleep -Seconds 10 # delay for vpn initialization

$StartPingerCommand = "nordvpn d && dotnet HSMPingModule/HSMPingModule.dll"
docker exec -u root -ti -d $ContainerId sh -c $StartPingerCommand