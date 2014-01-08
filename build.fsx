// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#r @"packages/FAKE/tools/FakeLib.dll"
open System
open Fake 
open Fake.AssemblyInfoFile
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

// The name of the project 
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "Frank"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "A functional web application DSL for ASP.NET Web API."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = """
  Frank defines a set of functions for building web applications in the
  functional style using types defined in System.Net.Http. Frank also
  includes adapters to host applications using System.Web.Routing."""
// List of author names (for NuGet package)
let authors = [ "Ryan Riley" ]
// Tags for your project (for NuGet package)
let tags = "F# fsharp web http rest webapi"

// File system information 
// (<projectFile>.*proj is built during the building process)
let projectFile = "Frank"
// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "bin/Frank*Tests*exe"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted 
let gitHome = "https://github.com/frank-fs"
// The name of the project on GitHub
let gitName = "frank"
// The git repository used to host the website.
let gitWebsite = "https://github.com/frank-fs/frank-fs.github.io"

// --------------------------------------------------------------------------------------
// The rest of the file includes standard build steps 
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES.md")

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
  let fileName = "src/" + project + "/AssemblyInfo.fs"
  CreateFSharpAssemblyInfo fileName
      [ Attribute.Title project
        Attribute.Product project
        Attribute.Description summary
        Attribute.Version release.AssemblyVersion
        Attribute.FileVersion release.AssemblyVersion ] )

// --------------------------------------------------------------------------------------
// Clean build results & restore NuGet packages

Target "RestorePackages" RestorePackages

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"])

Target "CleanDocs" (fun _ ->
    CleanDirs ["docs/output"])

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    !! ("*/**/" + projectFile + "*.*proj")
    |> MSBuildRelease "bin" "Rebuild"
    |> ignore)

Target "CopyLicense" (fun _ ->
    [ "LICENSE.txt" ] |> CopyTo "bin")

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target "RunTests" (fun _ ->
    !! testAssemblies
    |> NUnit (fun p ->
        { p with
            DisableShadowCopy = true
            TimeOut = TimeSpan.FromMinutes 20.
            OutputFile = "TestResults.xml" }))

// --------------------------------------------------------------------------------------
// Build a NuGet package

let referenceDependencies dependencies =
    let packagesDir = __SOURCE_DIRECTORY__  @@ "packages"
    [ for dependency in dependencies -> dependency, GetPackageVersion packagesDir dependency ]

Target "NuGet" (fun _ ->
    NuGet (fun p -> 
        { p with   
            Authors = authors
            Project = project
            Summary = summary
            Description = description
            Version = release.NugetVersion
            ReleaseNotes = String.Join(Environment.NewLine, release.Notes)
            Tags = tags
            OutputPath = "bin"
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey"
            Dependencies = referenceDependencies ["FSharp.Core.3"; "FSharpx.Core"; "Microsoft.AspNet.WebApi.Core"] })
        ("nuget/" + project + ".nuspec"))

// --------------------------------------------------------------------------------------
// Generate the documentation

Target "GenerateDocs" (fun _ ->
    executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"] [] |> ignore)

// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" (fun _ ->
    let ghPages = "gh-pages"
    CleanDir ghPages
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") ghPages ghPages

    fullclean ghPages
    CopyRecursive "docs/output" ghPages true |> tracefn "%A"
    StageAll ghPages
    Commit ghPages (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push ghPages)

Target "PublishWebsite" (fun _ ->
    let ghPages = "website"
    CleanDir ghPages
    Repository.clone "" (gitWebsite + ".git") ghPages

    fullclean ghPages
    CopyRecursive "docs/output" ghPages true |> tracefn "%A"
    CopyFile ghPages "CNAME" |> tracefn "%A"
    StageAll ghPages
    Commit ghPages (sprintf "Publish website for version %s" release.NugetVersion)
    Branches.push ghPages)

Target "Release" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  ==> "RestorePackages"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyLicense"
  ==> "RunTests"
  ==> "NuGet"
  ==> "All"

"All" 
  ==> "CleanDocs"
  ==> "GenerateDocs"
  ==> "Release"
  ==> "ReleaseDocs"
  ==> "PublishWebsite"

RunTargetOrDefault "All"

//Target "CreateWebHttpNuGet" (fun _ ->
//    XCopy (sources @@ "System.Web.Http.fs") nugetWebHttpContent
//
//    let webApiVersion = GetPackageVersion packagesDir "Microsoft.AspNet.WebApi.Core"
//
//    NuGet (fun p ->
//        {p with
//            Authors = authors
//            Project = "FSharp.Web.Http"
//            Description = "F# extensions for System.Web.Http"
//            Version = version
//            WorkingDir = nugetWebHttpDir
//            OutputPath = nugetWebHttpDir
//            ToolPath = nugetPath
//            Dependencies = ["Microsoft.AspNet.WebApi.Core", webApiVersion
//                            "FSharp.Net.Http", version]
//            AccessKey = getBuildParamOrDefault "nugetkey" ""
//            Publish = hasBuildParam "nugetkey" })
//        "frank.nuspec"
//
//    !! (nugetWebHttpDir @@ sprintf "FSharp.Web.Http.%s.nupkg" version)
//        |> CopyTo deployDir
//)
//
//Target "CreateFrankNuGet" (fun _ ->
//    XCopy (sources @@ "Frank.fs") nugetFrankContent
//
//    let fsharpxCoreVersion = GetPackageVersion packagesDir "FSharpx.Core"
//
//    NuGet (fun p ->
//        {p with
//            Authors = authors
//            Project = projectName
//            Description = projectDescription
//            Version = version
//            WorkingDir = nugetFrankDir
//            OutputPath = nugetFrankDir
//            ToolPath = nugetPath
//            Dependencies = ["FSharpx.Core", fsharpxCoreVersion
//                            "FSharp.Web.Http", version]
//            AccessKey = getBuildParamOrDefault "nugetkey" ""
//            Publish = hasBuildParam "nugetkey" })
//        "frank.nuspec"
//
//    !! (nugetFrankDir @@ sprintf "Frank.%s.nupkg" version)
//        |> CopyTo deployDir
//)
