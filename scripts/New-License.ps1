param(
    [Parameter(Mandatory = $true)]
    [string]$CustomerName,
    [string]$CustomerId = "",
    [string]$ContactEmail = "",
    [ValidateSet("pro", "demo")]
    [string]$Edition = "pro",
    [int]$SupportMonths = 12,
    [int]$DemoDays = 30,
    [string]$LicenseId = "",
    [string]$Product = "AnonPDFPro",
    [string]$PrivateKeyPath = ".\\private\\keys\\license_private.xml",
    [string]$PublicKeyPath = ".\\private\\keys\\license_public.xml",
    [string]$OutputDir = ".\\private\\licenses",
    [string]$ServerBaseUrl = "https://misart.pl/anonpdfpro",
    [string]$DefaultTheme = "",
    [string]$KeyId = "",
    [string[]]$Features = @(),
    [string]$MaxVersion = "",
    [string]$LatestVersion = "",
    [string]$DownloadUrl = "",
    [string]$StatusMessage = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-ToSlug {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "client"
    }
    $slug = $Value.ToLowerInvariant()
    $slug = $slug -replace "[^a-z0-9]+", "-"
    $slug = $slug.Trim("-")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return "client"
    }
    return $slug
}

if ([string]::IsNullOrWhiteSpace($LicenseId)) {
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd")
    $short = [Guid]::NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()
    $LicenseId = "LIC-$stamp-$short"
}

$issueDate = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
$perpetualUse = $Edition -eq "pro"
$supportUntil = $null
$updatesUntil = $null
$demoUntil = $null

if ($Edition -eq "pro") {
    $supportUntil = (Get-Date).ToUniversalTime().AddMonths($SupportMonths).ToString("yyyy-MM-dd")
    $updatesUntil = $supportUntil
} else {
    $demoUntil = (Get-Date).ToUniversalTime().AddDays($DemoDays).ToString("yyyy-MM-dd")
}

$payload = [ordered]@{
    licenseId = $LicenseId
    product = $Product
    edition = $Edition
    customerName = $CustomerName
    customerId = $CustomerId
    contactEmail = $ContactEmail
    issueDate = $issueDate
    perpetualUse = $perpetualUse
    supportUntil = $supportUntil
    updatesUntil = $updatesUntil
    demoUntil = $demoUntil
    features = $Features
    maxVersion = $MaxVersion
}

if (-not (Test-Path $PrivateKeyPath)) {
    throw "Private key not found: $PrivateKeyPath"
}

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
try {
    $rsa.FromXmlString([IO.File]::ReadAllText($PrivateKeyPath))
    $payloadJson = $payload | ConvertTo-Json -Depth 6 -Compress
    $dataBytes = [Text.Encoding]::UTF8.GetBytes($payloadJson)
    $hashAlg = [System.Security.Cryptography.SHA256]::Create()
    try {
        $signatureBytes = $rsa.SignData($dataBytes, $hashAlg)
    } finally {
        $hashAlg.Dispose()
    }
} finally {
    $rsa.Dispose()
}

$signature = [Convert]::ToBase64String($signatureBytes)

$license = [ordered]@{
    signatureAlgorithm = "RSA-SHA256"
    payload = $payload
    signature = $signature
}

if (-not [string]::IsNullOrWhiteSpace($KeyId)) {
    $license.keyId = $KeyId
}

$clientSlug = Convert-ToSlug $CustomerName
$outDir = Join-Path $OutputDir $clientSlug
$clientDir = Join-Path $outDir "client"
$serverDir = Join-Path $outDir "server"
New-Item -ItemType Directory -Force -Path $clientDir, $serverDir | Out-Null
$clientEditionDir = Join-Path $clientDir $Edition
New-Item -ItemType Directory -Force -Path $clientEditionDir | Out-Null

$licensePath = Join-Path $clientEditionDir "license.json"
$license | ConvertTo-Json -Depth 6 | Set-Content -Path $licensePath -Encoding UTF8

$status = [ordered]@{
    licenseId = $LicenseId
    updatesUntil = $updatesUntil
    revoked = $false
    latestVersion = $LatestVersion
    downloadUrl = $DownloadUrl
    message = $StatusMessage
}

$statusPath = Join-Path $serverDir ("{0}.json" -f $LicenseId)
$status | ConvertTo-Json -Depth 6 | Set-Content -Path $statusPath -Encoding UTF8

$config = [ordered]@{
    licenseFile = "license.json"
    publicKeyFile = "license_public.xml"
    serverBaseUrl = $ServerBaseUrl
    defaultTheme = $DefaultTheme
    licenseId = $LicenseId
}

$configPath = Join-Path $clientEditionDir "config.json"
$config | ConvertTo-Json -Depth 4 | Set-Content -Path $configPath -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($PublicKeyPath) -and (Test-Path $PublicKeyPath)) {
    Copy-Item -Path $PublicKeyPath -Destination (Join-Path $clientEditionDir "license_public.xml") -Force
}

Write-Host "License generated: $licensePath"
Write-Host "Server file: $statusPath"
Write-Host "Config file: $configPath"
