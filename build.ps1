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

dotnet test test/Frank.Tests
dotnet pack -c Release src/Frank /p:Version=$version$versionSuffix -o $psscriptroot/bin

dotnet test test/Frank.Analyzers.Tests
dotnet pack -c Release src/Frank.Analyzers /p:Version=$version$versionSuffix -o $psscriptroot/bin

dotnet test test/Frank.Auth.Tests
dotnet pack -c Release src/Frank.Auth /p:Version=$version$versionSuffix -o $psscriptroot/bin

dotnet test test/Frank.OpenApi.Tests
dotnet pack -c Release src/Frank.OpenApi /p:Version=$version$versionSuffix -o $psscriptroot/bin

dotnet test test/Frank.Datastar.Tests
dotnet pack -c Release src/Frank.Datastar /p:Version=$version$versionSuffix -o $psscriptroot/bin

# v7.3.2 in-development packages (Frank.Semantic, Frank.Validation, Frank.LinkedData,
# Frank.Discovery, Frank.Provenance, Frank.Cli.*) — test only; not yet published, so no pack.
dotnet test test/Frank.Semantic.Tests
dotnet test test/Frank.Validation.Tests
dotnet test test/Frank.LinkedData.Tests
dotnet test test/Frank.Discovery.Tests
dotnet test test/Frank.Provenance.Tests
dotnet test test/Frank.Cli.Core.Tests
dotnet test test/Frank.Cli.MSBuild.Tests
