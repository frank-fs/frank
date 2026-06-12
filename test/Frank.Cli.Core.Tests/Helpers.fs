module Frank.Cli.Core.Tests.Helpers

open System
open System.IO

let minimalFsproj (sourceFiles: string list) =
    let compileItems =
        sourceFiles
        |> List.map (fun f -> sprintf "    <Compile Include=\"%s\" />" (Path.GetFileName f))
        |> String.concat "\n"

    sprintf
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
%s
  </ItemGroup>
</Project>"""
        compileItems

/// Create a temp directory with the given source files and a minimal .fsproj.
/// Returns the path to the .fsproj file.
let createTempProject (sources: (string * string) list) : string =
    let dir = Path.Combine(Path.GetTempPath(), "frank_cli_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    let sourceFiles =
        sources
        |> List.map (fun (name, content) ->
            let path = Path.Combine(dir, name)
            File.WriteAllText(path, content)
            path)
    let fsproj = Path.Combine(dir, "TestProject.fsproj")
    File.WriteAllText(fsproj, minimalFsproj sourceFiles)
    fsproj

let deleteTempProject (fsproj: string) =
    try
        let dir = Path.GetDirectoryName(fsproj)
        Directory.Delete(dir, true)
    with _ ->
        ()
