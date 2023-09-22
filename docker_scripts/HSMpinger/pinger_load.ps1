$Version = $args[0]
$Repository = "hsmonitoring/hsmpingmodule"
$ExpectedImageTag = "${Repository}:$Version"

Write-Host "Load image v.$Version"
docker pull $ExpectedImageTag

Write-Host "Removing all stopped/unused containers"
docker container prune -f

Write-Host "Running pinger_run.ps1 script"
Invoke-Expression ".\pinger_run.ps1 $Version"