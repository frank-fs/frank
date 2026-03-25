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

New-Item -ItemType Directory -Force -Path $psscriptroot/nupkg | Out-Null
dotnet pack -c Release src/Frank.Cli.MSBuild -o $psscriptroot/nupkg

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

dotnet test test/Frank.Cli.Core.Tests
dotnet test test/Frank.Cli.IntegrationTests
dotnet test test/Frank.LinkedData.Tests
dotnet test test/Frank.LinkedData.Sample.Tests
dotnet test test/Frank.Provenance.Tests
dotnet test test/Frank.Validation.Tests
dotnet test test/Frank.Resources.Model.Tests
dotnet test test/Frank.Statecharts.Tests
dotnet test test/Frank.Statecharts.Sqlite.Tests
dotnet test test/Frank.Discovery.Tests
dotnet test test/Frank.TicTacToe.Tests
dotnet pack -c Release src/Frank.Resources.Model /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Statecharts.Core /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.LinkedData /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Discovery /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Provenance /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Validation /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Statecharts /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Statecharts.Sqlite /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Cli.Core /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Cli /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack -c Release src/Frank.Cli.MSBuild -o $psscriptroot/bin
