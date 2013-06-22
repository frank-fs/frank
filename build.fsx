#r "./packages/FAKE.1.64.6/tools/FakeLib.dll"

open Fake 

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
let testDir = "./test/"
let deployDir = "./deploy/"

let targetPlatformDir = getTargetPlatformDir "4.0.30139"

let nugetDir = "./nuget/"
let nugetLibDir = nugetDir @@ "lib/net40"

// params
let target = getBuildParamOrDefault "target" "All"

// tools
let fakePath = "./packages/FAKE.1.64.6/tools"
let nugetPath = "./.nuget/nuget.exe"
let nunitPath = "./packages/NUnit.Runners.2.6.0.12051/tools"

// files
let appReferences =
    !+ "./src/*.fsproj" 
        |> Scan

let testReferences =
    !+ "./src/*.fsproj"
        |> Scan

let filesToZip =
    !+ (buildDir + "/**/*.*")     
        -- "*.zip"
        |> Scan      

// targets
Target "Clean" (fun _ ->
    CleanDirs [buildDir; testDir; deployDir; nugetDir; nugetLibDir]
)

Target "BuildApp" (fun _ ->
    AssemblyInfo (fun p -> 
        {p with
           CodeLanguage = FSharp
           AssemblyVersion = version
           AssemblyTitle = projectName
           AssemblyDescription = projectDescription
           Guid = "5017411A-CF26-4E1A-85D6-1C49470C5996"
           OutputFileName = "./src/AssemblyInfo.fs"})

    MSBuildRelease buildDir "Build" appReferences
        |> Log "AppBuild-Output: "
)

Target "BuildTest" (fun _ -> 
    MSBuildDebug testDir "Build" testReferences
        |> Log "TestBuild-Output: "
)

Target "Test" (fun _ ->
    !+ (testDir + "Frank.dll")
        |> Scan
        |> NUnit (fun p -> 
            {p with 
               ToolPath = nunitPath; 
               DisableShadowCopy = true; 
               OutputFile = testDir + "TestResults.xml" }) 
)

Target "CopyLicense" (fun _ ->
    [ "LICENSE.txt" ] |> CopyTo buildDir
)

Target "BuildNuGet" (fun _ ->
    [buildDir + "Frank.dll"]
      |> CopyTo nugetLibDir

    let webApiVersion = GetPackageVersion packagesDir "Microsoft.AspNet.WebApi.Core"
    let fsharpxCoreVersion = GetPackageVersion packagesDir "FSharpx.Core"
    let fsharpxHttpVersion = GetPackageVersion packagesDir "FSharpx.Http"
    let impromptuInterfaceVersion = GetPackageVersion packagesDir "ImpromptuInterface"
    let impromptuInterfaceFSharpVersion = GetPackageVersion packagesDir "ImpromptuInterface.FSharp"

    NuGet (fun p ->
        {p with
            Authors = authors
            Project = projectName
            Description = projectDescription
            Version = version
            OutputPath = nugetDir
            Dependencies = ["Microsoft.AspNet.WebApi.Core",RequireExactly webApiVersion
                            "FSharpx.Core",RequireExactly fsharpxCoreVersion
                            "FSharpx.Http",RequireExactly fsharpxHttpVersion
                            "ImpromptuInterface",RequireExactly impromptuInterfaceVersion
                            "ImpromptuInterface.FSharp",RequireExactly impromptuInterfaceFSharpVersion ]
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            ToolPath = nugetPath
            Publish = hasBuildParam "nugetkey" })
        "frank.nuspec"

    [nugetDir + sprintf "Frank.%s.nupkg" version]
        |> CopyTo deployDir
)

Target "DeployZip" (fun _ ->    
    !! (buildDir + "/**/*.*")
    |> Zip buildDir (deployDir + sprintf "%s-%s.zip" projectName version)
)

FinalTarget "CloseTestRunner" (fun _ ->
    ProcessHelper.killProcess "nunit-agent.exe"
)

Target "Deploy" DoNothing
Target "All" DoNothing

// Build order
"Clean"
  ==> "BuildApp" <=> "BuildTest" <=> "CopyLicense"
  ==> "Test"
  ==> "BuildNuGet"
  ==> "DeployZip"
  ==> "Deploy"

"All" <== ["Deploy"]

// Start build
Run target
