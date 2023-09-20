$Version = $args[0]
$ContainerPrefix = "HSMPingModule"
$Repository = "hsmonitoring/hsmpingmodule"

$ExpectedImageTag = "${Repository}:$Version"
$FullContainerName = "${ContainerPrefix}_$Version"

$NetworkName = "HSM-Ping-network"
$ServerPrefix = "HSMServer_"

$ServerHost = ""
$NordVpnToken = ""

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

#--net=host
$ContainerId = docker run --user 0 -ti -d --name $FullContainerName --cap-add=NET_ADMIN --net=HSM-Ping-network --cap-add=NET_RAW -e TOKEN= -e TECHNOLOGY=NordLynx -v $LogsFolder -v $ConfigFolder -v $SecondConfig $ExpectedImageId

# docker network create $NetworkName

# $ServerContainerId = docker ps -q -f "name=$ServerPrefix"
# if (!$ServerContainerId)
# {
#     Write-Host "Running HSM server container hasn't be found"
#     exit
# }

# docker network connect $NetworkName $ServerContainerId --alias localhost

# Start-Sleep -Seconds 10 # delay for vpn initialization 
# docker network connect $NetworkName $FullContainerName

Start-Sleep -Seconds 10 # delay for network initialization 

$StartPingerCommand = "dotnet HSMPingModule/HSMPingModule.dll"
docker exec -u root -ti -d $ContainerId sh -c $StartPingerCommand
