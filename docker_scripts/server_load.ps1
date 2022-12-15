$Version = $args[0]
$Repository = "hsmonitoring/hierarchical_sensor_monitoring"
$ExpectedImageTag = "${Repository}:$Version"

Write-Host "Find and remove image with the same tag"
docker rmi $(docker images --filter=reference=$ExpectedImageTag -q) -f

Write-Host "Load image v.$Version"
docker pull $ExpectedImageTag

$ExpectedImageId = docker images --filter=reference=$ExpectedImageTag -q
Write-Host "Image id = $ExpectedImageId"

Write-Host "Removing all stopped/unused containers"
docker container prune

Write-Host "Runnig server_startup.ps1 script"
Invoke-Expression ".\server_startup.ps1 $Version"