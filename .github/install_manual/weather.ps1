Remove-Variable * -ErrorAction SilentlyContinue; Remove-Module *; $error.Clear();

function GetWeatherFromSource{
     param([string]$url = "https://api.open-meteo.com/v1/forecast?latitude=52.52&longitude=13.41&hourly=temperature_2m");
     $headers = @{"Content-Type" = "application/json" }
     $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
     $temperature = $response.hourly | Select-Object -ExpandProperty temperature_2m;
     $timestamps = $response.hourly | Select-Object -ExpandProperty time;
     return $temperature, $timestamps
}


function UploadDataToHSM{
    param([string]$productKey, [string]$address, [string]$path, [string]$time, [double]$value, [int]$status, [string]$comment);
   
    $headers = @{"Content-Type" = "application/json" }
    $fulladdress = $address + "/api/Sensors/double"
    #$body = @{ "key" = $productKey; "path" = $path; "time" = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fff"; "status" = $status; "comment" = $comment; "value" = $value} | ConvertTo-Json # тело запроса в формате JSON
    $body = @{ "key" = $productKey; "path" = $path; "time" = $time; "status" = $status; "comment" = $comment; "value" = $value} | ConvertTo-Json # тело запроса в формате JSON
    $response = Invoke-RestMethod -Uri $fulladdress -Headers $headers -Method Post -Body $body
}



$productKey1 = "product_key"
$address1 = "https://localhost:44333"
$path1 = "SwapTest/Test"
$status1 = 1
$comment1 = "test"

$data = GetWeatherFromSource "https://api.open-meteo.com/v1/forecast?latitude=52.52&longitude=13.41&hourly=temperature_2m"
$weatherDataLength = $data[0].Length
for ($i = 0; $i -lt $weatherDataLength; $i += 1)
{
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
    Write-Host "$($i + 1) from $weatherDataLength was uploaded"
    UploadDataToHSM $productKey1 $address1 $path1 $data[1][$i] $data[0][$i] $status1 $comment1
}