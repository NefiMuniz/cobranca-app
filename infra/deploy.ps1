# Builds and deploys the whole app to AWS.
# Requirements: AWS CLI (configured with `aws configure`), AWS SAM CLI, .NET 10 SDK, Node 20+.
# First run: .\infra\deploy.ps1 -Guided   (SAM asks for the stack name, region and the secret parameters)
# Next runs: .\infra\deploy.ps1
param(
    [switch]$Guided,
    [string]$StackName = "cobranca",
    # Your WhatsApp with DDD, shown to subscribers whose access is about to expire (optional).
    [string]$SupportWhatsApp = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> Backend tests" -ForegroundColor Cyan
dotnet test backend/Cobranca.slnx
if ($LASTEXITCODE -ne 0) { throw "Tests failed; nothing was deployed." }

Write-Host "==> SAM build" -ForegroundColor Cyan
sam build --template-file infra/template.yaml
if ($LASTEXITCODE -ne 0) { throw "sam build failed." }

Write-Host "==> SAM deploy" -ForegroundColor Cyan
if ($Guided) {
    sam deploy --guided --stack-name $StackName --capabilities CAPABILITY_IAM
} else {
    sam deploy --stack-name $StackName --capabilities CAPABILITY_IAM --no-fail-on-empty-changeset
}
if ($LASTEXITCODE -ne 0) { throw "sam deploy failed." }

function Get-Output([string]$key) {
    aws cloudformation describe-stacks --stack-name $StackName `
        --query "Stacks[0].Outputs[?OutputKey=='$key'].OutputValue" --output text
}

$apiUrl = (Get-Output "ApiUrl").TrimEnd("/")
$bucket = Get-Output "FrontendBucketName"
$distribution = Get-Output "DistributionId"
$frontendUrl = Get-Output "FrontendUrl"

Write-Host "==> Frontend build (API: $apiUrl)" -ForegroundColor Cyan
Push-Location frontend
try {
    npm ci
    $env:VITE_API_BASE_URL = $apiUrl
    $env:VITE_SUPPORT_WHATSAPP = $SupportWhatsApp
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed." }
} finally {
    Remove-Item Env:VITE_API_BASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:VITE_SUPPORT_WHATSAPP -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host "==> Upload to S3 and invalidate CloudFront" -ForegroundColor Cyan
aws s3 sync frontend/dist "s3://$bucket" --delete
aws cloudfront create-invalidation --distribution-id $distribution --paths "/*" | Out-Null

Write-Host ""
Write-Host "App:  $frontendUrl" -ForegroundColor Green
Write-Host "API:  $apiUrl/api/health" -ForegroundColor Green
