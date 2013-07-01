#if BOOT
open Fake
module FB = Fake.Boot
FB.Prepare {
    FB.Config.Default __SOURCE_DIRECTORY__ with
        NuGetDependencies =
            let (!!) x = FB.NuGetDependency.Create x
            [
                !!"FAKE"
                !!"NuGet.Build"
                !!"NuGet.Core"
                !!"NUnit.Runners"
            ]
}
#endif

#load ".build/boot.fsx"

open System.IO
open Fake 
open Fake.AssemblyInfoFile
open Fake.MSBuild

// properties
let projectName = "Frank"
let version = if isLocalBuild then "0.8." + System.DateTime.UtcNow.ToString("yMMdd") else buildVersion
let projectSummary = "A functional web application hosting and routing domain-specific language."
let projectDescription = "A functional web application hosting and routing domain-specific language."
let authors = ["Ryan Riley"]
let mail = "ryan@frankfs.net"
let homepage = "http://frankfs.net/"

// directories
let buildDir = "./build/"
let packagesDir = "./packages/"
let deployDir = "./deploy/"
let testDir = "./test/"
let nugetDir = "./nuget/"
let nugetLibDir = nugetDir @@ "lib/net40"
let template = __SOURCE_DIRECTORY__ @@ "template.html"
let sources = __SOURCE_DIRECTORY__ @@ "src"
let docsDir = __SOURCE_DIRECTORY__ @@ "docs"
let docRoot = getBuildParamOrDefault "docroot" homepage

// tools
let nugetPath = ".nuget/NuGet.exe"
let nunitPath = "packages/NUnit.Runners.2.6.2/tools"

// files
let appReferences =
    !+ "src/**/*.fsproj" 
        |> Scan

let testReferences =
    !+ "tests/**/*.fsproj"
        |> Scan

let filesToZip =
    !+ (buildDir + "/**/*.*")     
        -- "*.zip"
        |> Scan      

// targets
Target "Clean" (fun _ ->
    CleanDirs [buildDir; testDir; deployDir; nugetDir; nugetLibDir; docsDir]
)

Target "BuildApp" (fun _ ->
    if not isLocalBuild then
        [ Attribute.Version(buildVersion)
          Attribute.Title(projectName)
          Attribute.Description(projectDescription)
          Attribute.Guid("703a3f38-390d-47e4-9596-145e670d33df")
        ]
        |> CreateFSharpAssemblyInfo "src/FSharp.Net.Http/AssemblyInfo.fs"

    if not isLocalBuild then
        [ Attribute.Version(buildVersion)
          Attribute.Title(projectName)
          Attribute.Description(projectDescription)
          Attribute.Guid("853af415-e371-490b-9105-82427fcef2c0")
        ]
        |> CreateFSharpAssemblyInfo "src/FSharp.Web.Http/AssemblyInfo.fs"

    if not isLocalBuild then
        [ Attribute.Version(buildVersion)
          Attribute.Title(projectName)
          Attribute.Description(projectDescription)
          Attribute.Guid("5017411A-CF26-4E1A-85D6-1C49470C5996")
        ]
        |> CreateFSharpAssemblyInfo "src/Frank/AssemblyInfo.fs"

    MSBuildRelease buildDir "Build" appReferences
        |> Log "AppBuild-Output: "
)

Target "BuildTest" (fun _ -> 
    MSBuildDebug testDir "Build" testReferences
        |> Log "TestBuild-Output: "
)

Target "Test" (fun _ ->
    let nunitOutput = testDir + "TestResults.xml"
    !+ (testDir + "Frank.dll")
        |> Scan
        |> NUnit (fun p -> 
                    {p with 
                        ToolPath = nunitPath
                        DisableShadowCopy = true
                        OutputFile = nunitOutput})
)

Target "CopyLicense" (fun _ ->
    [ "LICENSE.txt" ] |> CopyTo buildDir
)

Target "BuildZip" (fun _ ->
    let zipFileName = deployDir + sprintf "%s-%s.zip" projectName version
    Zip buildDir zipFileName filesToZip
)

Target "CreateNuGet" (fun _ ->
    XCopy (buildDir.Trim('/')) nugetLibDir

    let webApiVersion = GetPackageVersion packagesDir "Microsoft.AspNet.WebApi.Core"
    let fsharpxCoreVersion = GetPackageVersion packagesDir "FSharpx.Core"

    NuGet (fun p ->
        {p with
            Authors = authors
            Project = projectName
            Description = projectDescription
            Version = version
            OutputPath = nugetDir
            ToolPath = nugetPath
            Dependencies = ["Microsoft.AspNet.WebApi.Core",RequireExactly webApiVersion
                            "FSharpx.Core",RequireExactly fsharpxCoreVersion ]
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" })
        "frank.nuspec"

    !! (nugetDir + sprintf "Frank.%s.nupkg" version)
        |> CopyTo deployDir
)

FinalTarget "CloseTestRunner" (fun _ ->
    ProcessHelper.killProcess "nunit-agent.exe"
)

Target "Deploy" DoNothing
Target "Default" DoNothing

// Build order
"Clean"
    ==> "BuildApp" <=> "BuildTest" <=> "CopyLicense"
    ==> "Test"
    ==> "BuildZip"
    ==> "CreateNuGet"
    ==> "Deploy"

"Default" <== ["Deploy"]

// Start build
RunTargetOrDefault "Default"

