param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $projectRoot 'src\XiliPomodoro\XiliPomodoro.csproj'
$destination = Join-Path $projectRoot 'dist\惜立番茄钟'
$version = ([xml](Get-Content (Join-Path $projectRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
$stage = Join-Path $projectRoot ('artifacts\publish-' + [guid]::NewGuid().ToString('N'))
$archiveDirectory = Join-Path $projectRoot 'artifacts\packages'
$archive = Join-Path $archiveDirectory "YuudachiPomodoro-$version-win-x64.zip"

function Assert-WorkspacePath([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (!$absolute.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside workspace: $absolute"
    }
}

if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $destination '惜立番茄钟.exe') }) {
    throw '请先在应用设置中点击保存并退出，再重新发布。'
}
if (!$NoRestore) {
    dotnet restore $project --packages (Join-Path $projectRoot '.packages') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
}
try {
    dotnet publish $project -c Release --no-restore -o $stage -p:EnableUiTests=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    foreach ($required in @('惜立番茄钟.exe', '惜立番茄钟.dll', 'App.xbf', '惜立番茄钟.pri', 'Microsoft.ui.xaml.dll', 'Microsoft.Graphics.Canvas.dll', 'Microsoft.Graphics.Canvas.Interop.dll', 'e_sqlite3.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Assets\XiliPomodoro.ico')) {
        if (!(Test-Path -LiteralPath (Join-Path $stage $required))) { throw "Missing release file: $required" }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $stage -Recurse
    # Retain licenses shipped by each resolved dependency, including the bundled .NET runtime.
    $assets = Get-Content (Join-Path $projectRoot 'src\XiliPomodoro\obj\project.assets.json') -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $packagePath = Join-Path $folder $library.Value.path
            if (!(Test-Path -LiteralPath $packagePath)) { continue }
            $notices = Get-ChildItem -LiteralPath $packagePath -File | Where-Object Name -Match '(license|notice|copying)'
            if ($notices) {
                $licensePath = Join-Path $stage ('licenses\' + $library.Name.Replace('/', '-'))
                New-Item -ItemType Directory -Force -Path $licensePath | Out-Null
                $notices | Copy-Item -Destination $licensePath
            }
            break
        }
    }
    # Always archive a fresh build, never a directory used for real user data.
    if (Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -match '\.(db|db-wal|db-shm|log|pdb)$' -or $_.FullName -match '[\\/]data[\\/]' }) {
        throw 'Release staging unexpectedly contains user data or diagnostic output.'
    }
    New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
    $checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$checksum  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii

    # Replace the sole local app, preserving its portable profile.
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $destination -Force) {
        if ($item.Name -eq 'data') { continue }
        Assert-WorkspacePath $item.FullName
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
    Get-ChildItem -LiteralPath $stage -Force | Copy-Item -Destination $destination -Recurse -Force
    Write-Output "Ready: $destination\惜立番茄钟.exe"
    Write-Output "Archive: $archive"
    Write-Output "SHA256: $checksum"
} finally {
    Assert-WorkspacePath $stage
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
