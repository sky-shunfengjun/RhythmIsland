param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$themeRoot = Join-Path $repositoryRoot "src\RhythmIsland.Theme"
$artifactRoot = Join-Path $repositoryRoot "artifacts"
$artifactPath = Join-Path $artifactRoot "RhythmIsland.Theme.zip"
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RhythmIsland.Theme." + [Guid]::NewGuid().ToString("N"))
$requiredThemeFiles = @("manifest.yml", "Styles.axaml", "README.md", "banner.png")

try {
    foreach ($fileName in $requiredThemeFiles) {
        $sourcePath = Join-Path $themeRoot $fileName
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Missing required theme file: $fileName"
        }
    }

    [xml]$styles = Get-Content -LiteralPath (Join-Path $themeRoot "Styles.axaml") -Raw -Encoding UTF8
    if ($styles.DocumentElement.LocalName -ne "Styles") {
        throw "Styles.axaml root element must be Styles."
    }

    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    foreach ($fileName in $requiredThemeFiles) {
        Copy-Item -LiteralPath (Join-Path $themeRoot $fileName) -Destination (Join-Path $stagingRoot $fileName)
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination (Join-Path $stagingRoot "LICENSE")

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    if (Test-Path -LiteralPath $artifactPath) { Remove-Item -LiteralPath $artifactPath -Force }
    Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $artifactPath -CompressionLevel Optimal
    Write-Output $artifactPath
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}
