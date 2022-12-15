$Version = $args[0]
$Repository = "hsmonitoring/hierarchical_sensor_monitoring"
$ExpectedImageTag = "${Repository}:$Version"

Write-Host "Find and remove image with the same tag"
docker rmi $(docker images --filter=reference=$ExpectedImageTag -q) -f

Write-Host "Load image v.$Version"
docker pull $ExpectedImageTag

Write-Host "Removing all stopped/unused containers"
docker container prune -f

Write-Host "Running server_startup.ps1 script"
Invoke-Expression ".\server_startup.ps1 $Version"