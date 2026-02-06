[xml]$doc = Get-Content .\src\Directory.Build.props
$version = $doc.Project.PropertyGroup.VersionPrefix # the version under development, update after a release
$versionSuffix = '-build.0' # manually incremented for local builds

function isVersionTag($tag){
    $v = New-Object Version
    [Version]::TryParse($tag, [ref]$v)
}

if ($env:appveyor){
    $versionSuffix = '-build.' + $env:appveyor_build_number
    if ($env:appveyor_repo_tag -eq 'true' -and (isVersionTag($env:appveyor_repo_tag_name))){
        $version = $env:appveyor_repo_tag_name
        $versionSuffix = ''
    }
    Update-AppveyorBuild -Version "$version$versionSuffix"
}

dotnet build -c Release Frank.sln /p:Version=$version$versionSuffix
dotnet pack -c Release src/Frank /p:Version=$version$versionSuffix -o $psscriptroot/bin

dotnet test test/Frank.Analyzers.Tests
dotnet pack -c Release src/Frank.Analyzers /p:Version=$version$versionSuffix -o $psscriptroot/bin

dotnet test test/Frank.Auth.Tests
dotnet pack -c Release src/Frank.Auth /p:Version=$version$versionSuffix -o $psscriptroot/bin

dotnet build -c Release Frank.Datastar.sln /p:Version=$version$versionSuffix
dotnet test test/Frank.Datastar.Tests
dotnet pack -c Release src/Frank.Datastar /p:Version=$version$versionSuffix -o $psscriptroot/bin
