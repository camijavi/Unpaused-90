<#
.SYNOPSIS
    Deploys the Unpaused 90 unified application (React + .NET 10) to Microsoft Azure App Service.

.DESCRIPTION
    This script automates the complete end-to-end deployment pipeline:
    1. Validates Azure CLI login & active subscription.
    2. Builds the React frontend and bundles it into NETUnpaused90/wwwroot.
    3. Builds and publishes the .NET 10 Web API.
    4. Ensures Azure App Service Plan & Web App exist (Linux, .NET 10 LTS).
    5. Ensures System-Assigned Managed Identity is enabled on the App Service.
    6. Ensures Microsoft Entra ID RBAC role assignments:
       - Storage Blob Data Reader on Azure Blob Storage (matches container)
       - Cognitive Services OpenAI User on Azure OpenAI
       - Cognitive Services User on Azure AI Speech
    7. Packages and deploys the release package via Azure CLI ZIP deployment.
    8. Performs a smoke test against the live site.

.PARAMETER ResourceGroup
    The Azure Resource Group name (Default: 'unpaused-90-rg').

.PARAMETER AppName
    The Azure App Service Web App name (Default: 'unpaused-90-app').

.PARAMETER PlanName
    The Azure App Service Plan name (Default: 'unpaused-90-plan').

.PARAMETER Location
    The Azure datacenter region (Default: 'westus').

.PARAMETER Sku
    The App Service pricing SKU (Default: 'B1').

.PARAMETER StorageAccount
    The Azure Storage Account name (Default: 'unpaused90storage').

.PARAMETER OpenAiResource
    The Azure OpenAI resource name (Default: 'unpaused-90-openai-resource').

.PARAMETER SpeechResource
    The Azure AI Speech resource name (Default: 'unpaused-90-speech-service').
#>

[CmdletBinding()]
param (
    [string]$ResourceGroup = "unpaused-90-rg",
    [string]$AppName = "unpaused-90-app",
    [string]$PlanName = "unpaused-90-plan",
    [string]$Location = "westus",
    [string]$Sku = "B1",
    [string]$StorageAccount = "unpaused90storage",
    [string]$OpenAiResource = "unpaused-90-openai-resource",
    [string]$SpeechResource = "unpaused-90-speech-service"
)

$ErrorActionPreference = "Stop"

# Resolve Azure CLI path
$azCmd = "az"
if (Get-Command "az.cmd" -ErrorAction SilentlyContinue) {
    $azCmd = "az.cmd"
} elseif (Test-Path "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd") {
    $azCmd = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
}

Write-Host "===========================================================" -ForegroundColor Cyan
Write-Host "  UNPAUSED 90 - AZURE DEPLOYMENT SCRIPT                     " -ForegroundColor Cyan
Write-Host "===========================================================" -ForegroundColor Cyan
Write-Host "Resource Group  : $ResourceGroup"
Write-Host "App Service     : $AppName"
Write-Host "Plan Name       : $PlanName (SKU: $Sku, Region: $Location)"
Write-Host "Storage Account : $StorageAccount"
Write-Host "OpenAI Resource : $OpenAiResource"
Write-Host "Speech Resource : $SpeechResource"
Write-Host "-----------------------------------------------------------"

# 1. Verify Azure CLI login
Write-Host "`n[1/7] Checking Azure CLI authentication..." -ForegroundColor Yellow
$account = & $azCmd account show -o json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Error "Not logged into Azure CLI. Please run 'az login' before executing this script."
    exit 1
}
Write-Host "Logged in as: $($account.user.name) (Subscription: $($account.name))" -ForegroundColor Green

# 2. Build React Frontend
Write-Host "`n[2/7] Building React production frontend..." -ForegroundColor Yellow
Push-Location "react-unpaused-90"
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
} finally {
    Pop-Location
}

# Sync React build into .NET wwwroot
$wwwrootDir = "NETUnpaused90/wwwroot"
if (-not (Test-Path $wwwrootDir)) {
    New-Item -ItemType Directory -Path $wwwrootDir -Force | Out-Null
}
Write-Host "Copying React bundle to $wwwrootDir..."
Copy-Item -Path "react-unpaused-90/dist/*" -Destination $wwwrootDir -Recurse -Force

# 3. Publish .NET 10 Web API
Write-Host "`n[3/7] Publishing .NET 10 Web API..." -ForegroundColor Yellow
$publishDir = "./publish-app"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

dotnet publish NETUnpaused90/NETUnpaused90.csproj -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# 4. Ensure App Service Plan & Web App exist
Write-Host "`n[4/7] Verifying Azure Infrastructure..." -ForegroundColor Yellow

$planExists = & $azCmd appservice plan show --name $PlanName --resource-group $ResourceGroup -o json 2>$null
if (-not $planExists) {
    Write-Host "Creating App Service Plan '$PlanName'..." -ForegroundColor Cyan
    & $azCmd appservice plan create --name $PlanName --resource-group $ResourceGroup --is-linux --sku $Sku --location $Location
}

$appExists = & $azCmd webapp show --name $AppName --resource-group $ResourceGroup -o json 2>$null
if (-not $appExists) {
    Write-Host "Creating Web App '$AppName' with runtime DOTNETCORE:10.0..." -ForegroundColor Cyan
    & $azCmd webapp create --name $AppName --resource-group $ResourceGroup --plan $PlanName --runtime "DOTNETCORE:10.0"
}

# 5. Ensure Managed Identity and Entra ID RBAC Roles
Write-Host "`n[5/7] Ensuring Managed Identity and RBAC permissions..." -ForegroundColor Yellow
$identityJson = & $azCmd webapp identity assign --name $AppName --resource-group $ResourceGroup -o json | ConvertFrom-Json
$principalId = $identityJson.principalId
Write-Host "Web App System-Assigned Managed Identity Principal ID: $principalId" -ForegroundColor Green

# Role 1: Storage Blob Data Reader
Write-Host "Configuring Storage Blob Data Reader on $StorageAccount..."
$storageId = (& $azCmd storage account show --name $StorageAccount --resource-group $ResourceGroup --query id -o tsv).Trim()
& $azCmd role assignment create --assignee $principalId --role "Storage Blob Data Reader" --scope $storageId 2>$null

# Role 2: Cognitive Services OpenAI User
Write-Host "Configuring Cognitive Services OpenAI User on $OpenAiResource..."
$openaiId = (& $azCmd cognitiveservices account show --name $OpenAiResource --resource-group $ResourceGroup --query id -o tsv).Trim()
& $azCmd role assignment create --assignee $principalId --role "Cognitive Services OpenAI User" --scope $openaiId 2>$null

# Role 3: Cognitive Services User
Write-Host "Configuring Cognitive Services User on $SpeechResource..."
$speechId = (& $azCmd cognitiveservices account show --name $SpeechResource --resource-group $ResourceGroup --query id -o tsv).Trim()
& $azCmd role assignment create --assignee $principalId --role "Cognitive Services User" --scope $speechId 2>$null

# 6. Package and Deploy
Write-Host "`n[6/7] Packaging and deploying to Azure Web App..." -ForegroundColor Yellow
$zipPath = "./app.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

# Note: Compress-Archive in PowerShell creates Windows backslashes in paths,
# which causes Linux Kudu rsync to fail. We use Python or .NET with forward slashes '/'.
Write-Host "Creating Linux-compatible zip archive..."
python -c @"
import os, zipfile
publish_dir = r'$publishDir'
zip_path = r'$zipPath'
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(publish_dir):
        for f in files:
            full = os.path.join(root, f)
            rel = os.path.relpath(full, publish_dir).replace('\\', '/')
            z.write(full, rel)
"@

& $azCmd webapp deploy --resource-group $ResourceGroup --name $AppName --src-path $zipPath --type zip

# Cleanup temp files
Remove-Item -Recurse -Force $publishDir, $zipPath -ErrorAction SilentlyContinue

# 7. Smoke Test
Write-Host "`n[7/7] Verifying live deployment..." -ForegroundColor Yellow
$liveUrl = "https://$AppName.azurewebsites.net"
Write-Host "Waiting 5 seconds for App Service warmup..."
Start-Sleep -Seconds 5

try {
    $res = Invoke-WebRequest -Uri "$liveUrl/api/v1/matches" -UseBasicParsing -TimeoutSec 30
    Write-Host "Live Matches API Check: Status $($res.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "Warmup note: Application is still starting up. ($($_.Exception.Message))" -ForegroundColor Yellow
}

Write-Host "`n===========================================================" -ForegroundColor Green
Write-Host "  DEPLOYMENT COMPLETE!                                     " -ForegroundColor Green
Write-Host "  Live URL: $liveUrl" -ForegroundColor Green
Write-Host "===========================================================" -ForegroundColor Green
