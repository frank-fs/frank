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
let deployDir = __SOURCE_DIRECTORY__ @@ "deploy"
let packagesDir = __SOURCE_DIRECTORY__ @@ "packages"
let testDir = __SOURCE_DIRECTORY__ @@ "test"
let nugetDir = __SOURCE_DIRECTORY__ @@ "nuget"
let nugetNetHttpDir = nugetDir @@ "FSharp.Net.Http" @@ "content"
let nugetWebHttpDir = nugetDir @@ "FSharp.Web.Http" @@ "content"
let nugetFrankDir = nugetDir @@ "Frank" @@ "content"
let template = __SOURCE_DIRECTORY__ @@ "template.html"
let sources = __SOURCE_DIRECTORY__ @@ "src"
let docsDir = __SOURCE_DIRECTORY__ @@ "docs"
let docRoot = getBuildParamOrDefault "docroot" homepage

// tools
let nugetPath = ".nuget/NuGet.exe"
let nunitPath = "packages/NUnit.Runners.2.6.2/tools"

// files
let testReferences =
    !+ "src/*.fsproj"
        |> Scan

// targets
Target "Clean" (fun _ ->
    CleanDirs [deployDir; testDir; nugetDir; nugetNetHttpDir; nugetWebHttpDir; nugetFrankDir; docsDir]
)

Target "BuildTest" (fun _ -> 
    MSBuildDebug testDir "Build" testReferences
        |> Log "TestBuild-Output: "
)

Target "Test" (fun _ ->
    let nunitOutput = testDir @@ "TestResults.xml"
    !+ (testDir @@ "Frank.dll")
        |> Scan
        |> NUnit (fun p -> 
                    {p with 
                        ToolPath = nunitPath
                        DisableShadowCopy = true
                        OutputFile = nunitOutput})
)

Target "CreateNetHttpNuGet" (fun _ ->
    XCopy (sources @@ "System.Net.Http.fs") nugetNetHttpDir

    let webApiVersion = GetPackageVersion packagesDir "Microsoft.AspNet.WebApi.Client"

    NuGet (fun p ->
        {p with
            Authors = authors
            Project = "FSharp.Net.Http"
            Description = "F# extensions for System.Net.Http"
            Version = version
            OutputPath = nugetNetHttpDir
            ToolPath = nugetPath
            Dependencies = ["Microsoft.AspNet.WebApi.Client", webApiVersion]
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" })
        "frank.nuspec"

    !! (nugetNetHttpDir @@ sprintf "FSharp.Net.Http.%s.nupkg" version)
        |> CopyTo deployDir
)

Target "CreateWebHttpNuGet" (fun _ ->
    XCopy (sources @@ "System.Web.Http.fs") nugetWebHttpDir

    let webApiVersion = GetPackageVersion packagesDir "Microsoft.AspNet.WebApi.Core"

    NuGet (fun p ->
        {p with
            Authors = authors
            Project = "FSharp.Web.Http"
            Description = "F# extensions for System.Web.Http"
            Version = version
            OutputPath = nugetWebHttpDir
            ToolPath = nugetPath
            Dependencies = ["Microsoft.AspNet.WebApi.Core", webApiVersion
                            "FSharp.Net.Http", version]
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" })
        "frank.nuspec"

    !! (nugetWebHttpDir @@ sprintf "FSharp.Web.Http.%s.nupkg" version)
        |> CopyTo deployDir
)

Target "CreateFrankNuGet" (fun _ ->
    XCopy (sources @@ "Frank.fs") nugetFrankDir

    let fsharpxCoreVersion = GetPackageVersion packagesDir "FSharpx.Core"

    NuGet (fun p ->
        {p with
            Authors = authors
            Project = projectName
            Description = projectDescription
            Version = version
            OutputPath = nugetFrankDir
            ToolPath = nugetPath
            Dependencies = ["FSharpx.Core", fsharpxCoreVersion
                            "FSharp.Web.Http", version]
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
    ==> "BuildTest"
    ==> "Test"
    ==> "CreateNetHttpNuGet"
    ==> "CreateWebHttpNuGet"
    ==> "CreateFrankNuGet"
    ==> "Deploy"

"Default" <== ["Deploy"]

// Start build
RunTargetOrDefault "Default"
