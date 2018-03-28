// -------------------------------------------------------------------------------------- // FAKE build script // -------------------------------------------------------------------------------------- #I "packages/FAKE/tools/"
#I "packages/build/FAKE/tools"
#r "FakeLib.dll"
open System
open Fake 
open Fake.Git
open Fake.ReleaseNotesHelper

// --------------------------------------------------------------------------------------
// Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package 
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted 
let gitHome = "git@github.com:frank-fs"
// The name of the project on GitHub
let gitName = "frank"
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/frank-fs"

let buildDir = IO.Path.Combine(Environment.CurrentDirectory, "bin")

// --------------------------------------------------------------------------------------
// The rest of the file includes standard build steps 
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let (!!) includes = (!! includes).SetBaseDirectory __SOURCE_DIRECTORY__
let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES.md")
let isAppVeyorBuild = environVar "APPVEYOR" |> isNull |> not
let nugetVersion = 
    if isAppVeyorBuild then
        let nugetVersion =
            let isTagged = Boolean.Parse(environVar "APPVEYOR_REPO_TAG")
            if isTagged then
                environVar "APPVEYOR_REPO_TAG_NAME"
            else
                sprintf "%s-b%03i" release.NugetVersion (int buildVersion)
        Shell.Exec("appveyor", sprintf "UpdateBuild -Version \"%s\"" nugetVersion) |> ignore
        nugetVersion
    else release.NugetVersion

Target "BuildVersion" (fun _ ->
    Shell.Exec("appveyor", sprintf "UpdateBuild -Version \"%s\"" nugetVersion) |> ignore
)

// --------------------------------------------------------------------------------------
// Clean build results & restore NuGet packages

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"]
)

Target "CleanDocs" (fun _ ->
    CleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    DotNetCli.Build (fun defaults ->
        { defaults with
            Project = "src/Frank"
            Configuration = "Release" })
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target "RunTests" (fun _ ->
    DotNetCli.Test (fun p ->
        { p with
            Project = "tests/Frank.Tests"
            Configuration = "Release"
            AdditionalArgs =
              [ yield "--test-adapter-path:."
                yield if isAppVeyorBuild then
                        sprintf "--logger:Appveyor"
                      else
                        sprintf "--logger:nunit;LogFileName=%s" (IO.Path.Combine(buildDir, "TestResults.xml")) ]
        })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "Pack" (fun _ ->
    DotNetCli.Pack (fun p ->
        { p with
            Project = "src/Frank"
            OutputPath = buildDir
            AdditionalArgs =
              [ "--no-build"
                sprintf "/p:Version=%s" nugetVersion
                //"/p:ReleaseNotes=" + (toLines release.Notes)
              ]
        })
)

Target "Push" (fun _ ->
    DotNetCli.Publish (fun p ->
        { p with WorkingDir = buildDir })
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target "GenerateReferenceDocs" (fun _ ->
    if not <| executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"; "--define:REFERENCE"] [] then
      failwith "generating reference documentation failed"
)

Target "GenerateHelp" (fun _ ->
    if not <| executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"; "--define:HELP"] [] then
      failwith "generating help documentation failed"
)

Target "GenerateDocs" DoNothing

// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    CleanDir tempDocsDir
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    CopyRecursive "docs/output" tempDocsDir true |> tracefn "%A"
    StageAll tempDocsDir
    Commit tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir
)

Target "Release" (fun _ ->
    StageAll ""
    Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion
)

Target "BuildPackage" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  =?> ("BuildVersion", isAppVeyorBuild)
  ==> "Build"
  ==> "RunTests"
  ==> "Pack"
  ==> "BuildPackage"
  ==> "All"
  =?> ("GenerateReferenceDocs",isLocalBuild && not isMono)
  =?> ("GenerateDocs",isLocalBuild && not isMono)
  =?> ("ReleaseDocs",isLocalBuild && not isMono)

"CleanDocs"
  ==> "GenerateHelp"
  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"
    
"ReleaseDocs"
  ==> "Release"

"All"
  ==> "Push"
  ==> "Release"

RunTargetOrDefault "All"
