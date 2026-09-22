Param(
    [string]$Version = "latest",
    [string]$BaseDirectory = ""
)

if ([string]::IsNullOrEmpty($BaseDirectory)) {
    $BaseDirectory = "/usr/HSM/" # Unix base directory
}

Write-Host "Version:" $Version
Write-Host "Base directory:" $BaseDirectory

$Repository = "hsmonitoring/hierarchical_sensor_monitoring"
$ExpectedImageTag = "${Repository}:$Version"
$ContainerName = "HSMServer"

function Start-ProcessMonitoring {
    param (
        [string]$ContainerId
    )
    
    $ProcessPID = 1
    $Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $DumpFile = "Logs/mem_${Timestamp}.dmp"
    
    Write-Host "Starting process monitoring for PID $ProcessPID"
    Write-Host "Dump file: $DumpFile"

    $ProcdumpCommand = "procdump 1 -e -f OutOfMemoryException -o `"$DumpFile`""
    Write-Host "Executing: $ProcdumpCommand"
    
    $Process = Start-Process -NoNewWindow -FilePath "bash" -ArgumentList "-c", "`"$ProcdumpCommand &`"" -PassThru
    Start-Sleep -Seconds 3

    $ProcdumpProcess = bash -c "ps aux | grep -v grep | grep 'procdump -e'"
    if ($ProcdumpProcess) {
        Write-Host "Procdump started successfully with PID: $($ProcdumpProcess.Split()[1])"
        return $true
    }
    
    Write-Host "ERROR: Failed to start procdump on host"
    return $false
}

Write-Host "Loading image v.$Version"
docker pull $ExpectedImageTag

Write-Host "Removing stopped containers"
docker container prune -f

$CurrentContainerId = docker ps -q -f "name=$ContainerName"
if ($CurrentContainerId) {
    Write-Host "Stopping existing container: $CurrentContainerId"
    docker stop $CurrentContainerId
    docker rm $CurrentContainerId
}

$ExpectedImageId = docker images --filter=reference=$ExpectedImageTag -q
if ($ExpectedImageId) {
    Write-Host "Starting new container..."
    
    $NewContainerId = docker run -d -it -u 0 --name "${ContainerName}_$Version" `
        -v "${BaseDirectory}Logs:/app/Logs" `
        -v "${BaseDirectory}Config:/app/Config" `
        -v "${BaseDirectory}Databases:/app/Databases" `
        -v "${BaseDirectory}DatabasesBackups:/app/DatabasesBackups" `
        -p "44330:44330" `
        -p "44333:44333" `
        $ExpectedImageId

    Write-Host "Container started with ID: $NewContainerId"

       try {
        $Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $DumpFile = "Logs/dump_${Timestamp}.dmp"
        
        Write-Host "Attempting to start procdump inside container..."

        # Create dump file dump_20250422_160501.dmp in Logs when OutOfMemoryException exception will fired
        $Command = "procdump 1 -e -f OutOfMemoryException -o $DumpFile &"
		    # procdump 1 -m 100  -e -f OutOfMemoryException  -o  Logs/mem.dmp  -> "[ERROR]: Signal/Exception trigger must be the only trigger specified."
		
        docker exec $NewContainerId bash -c $Command
        
        Start-Sleep -Seconds 2
        $Result = docker exec $NewContainerId bash -c "ps aux | grep -v grep | grep procdump"
        
        if ($Result) {
            # Write-Host "Procdump successfully started inside container: $Result"
			      Write-Host "Procdump successfully started inside container"
        } else {
            Write-Host "WARNING: Procdump might not be running inside container"
        }
    }
    catch {
        Write-Host "ERROR: Failed to start procdump - $_"
    }
}
else {
    Write-Host "ERROR: Expected image not found"
}
