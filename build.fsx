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
let version = if isLocalBuild then "0.9." + System.DateTime.UtcNow.ToString("yMMdd") else buildVersion
let projectSummary = "A functional web application hosting and routing domain-specific language."
let projectDescription = "A functional web application hosting and routing domain-specific language."
let authors = ["Ryan Riley"]
let mail = "ryan@frankfs.net"
let homepage = "http://frankfs.net/"

// directories
let buildDir = __SOURCE_DIRECTORY__ @@ "build"
let deployDir = __SOURCE_DIRECTORY__ @@ "deploy"
let packagesDir = __SOURCE_DIRECTORY__ @@ "packages"
let testDir = __SOURCE_DIRECTORY__ @@ "test"
let nugetDir = __SOURCE_DIRECTORY__ @@ "nuget"
let nugetNetHttpDir = nugetDir @@ "FSharp.Net.Http"
let nugetWebHttpDir = nugetDir @@ "FSharp.Web.Http"
let nugetFrankDir = nugetDir @@ "Frank"
let nugetNetHttpContent = nugetNetHttpDir @@ "content"
let nugetWebHttpContent = nugetWebHttpDir @@ "content"
let nugetFrankLib = nugetFrankDir @@ "lib/net40"
let template = __SOURCE_DIRECTORY__ @@ "template.html"
let sources = __SOURCE_DIRECTORY__ @@ "src"
let docsDir = __SOURCE_DIRECTORY__ @@ "docs"
let docRoot = getBuildParamOrDefault "docroot" homepage

// tools
let nugetPath = ".nuget/NuGet.exe"
let nunitPath = "packages/NUnit.Runners.2.6.2/tools"

// files
let srcReferences =
    !! "src/*.fsproj"

let testReferences =
    !! "tests/*.fsproj"

// targets
Target "Clean" (fun _ ->
    CleanDirs [deployDir
               docsDir
               testDir
               nugetDir
               nugetNetHttpDir
               nugetNetHttpContent
               nugetWebHttpDir
               nugetWebHttpContent
               nugetFrankDir
               nugetFrankLib]
)

Target "BuildApp" (fun _ -> 
    if not isLocalBuild then
        [ Attribute.Version(buildVersion)
          Attribute.Title(projectName)
          Attribute.Description(projectDescription)
          Attribute.Guid("020697d7-24a3-4ce4-a326-d2c7c204ffde")
        ]
        |> CreateFSharpAssemblyInfo "src/fracture/AssemblyInfo.fs"

    MSBuildDebug buildDir "Build" srcReferences
        |> Log "AppBuild-Output: "
)

Target "BuildTest" (fun _ -> 
    MSBuildDebug testDir "Build" testReferences
        |> Log "TestBuild-Output: "
)

Target "Test" (fun _ ->
    let nunitOutput = testDir @@ "TestResults.xml"
    !! (testDir @@ "Frank.dll")
        |> NUnit (fun p -> 
                    {p with 
                        ToolPath = nunitPath
                        DisableShadowCopy = true
                        OutputFile = nunitOutput})
)

Target "CopyLicense" (fun _ ->
    [ "LICENSE.txt" ] |> CopyTo buildDir
)

Target "CreateNuGet" (fun _ ->
    [ buildDir @@ "Frank.dll"
      buildDir @@ "Frank.pdb" ]
        |> CopyTo nugetFrankLib

    let fsharpCoreVersion = GetPackageVersion packagesDir "FSharp.Core.3"
    let fsharpxCoreVersion = GetPackageVersion packagesDir "FSharpx.Core"
    let webApiVersion = GetPackageVersion packagesDir "Microsoft.AspNet.WebApi.Client"

    NuGet (fun p ->
        {p with
            Authors = authors
            Project = projectName
            Description = projectDescription
            Version = version
            OutputPath = nugetFrankDir
            ToolPath = nugetPath
            Dependencies = ["FSharp.Core.3", fsharpCoreVersion
                            "FSharpx.Core", fsharpxCoreVersion
                            "Microsoft.AspNet.WebApi.Client", webApiVersion]
                            
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" })
        "frank.nuspec"

    !! (nugetFrankDir @@ sprintf "Frank.%s.nupkg" version)
        |> CopyTo deployDir
)

FinalTarget "CloseTestRunner" (fun _ ->
    ProcessHelper.killProcess "nunit-agent.exe"
)

Target "Deploy" DoNothing
Target "Default" DoNothing

// Build order
"Clean"
    ==> "BuildApp" <=> "CopyLicense" <=> "BuildTest"
    ==> "Test"
    ==> "CreateNuGet"
    ==> "Deploy"

"Default" <== ["Deploy"]

// Start build
RunTargetOrDefault "Default"
