#I "./packages/FAKE.1.56.7/tools"
#r "FakeLib.dll"

open Fake 
open System.IO

// properties
let projectName = "Frank"
let version = "0.3.0"  
let projectSummary = "A functional web application hosting and routing domain-specific language."
let projectDescription = "A functional web application hosting and routing domain-specific language."
let authors = ["Ryan Riley"]
let mail = "ryan@frankfs.net"
let homepage = "http://frankfs.net/"
let nugetKey = if System.IO.File.Exists "./key.txt" then ReadFileAsString "./key.txt" else ""

// directories
let buildDir = "./build/"
let packagesDir = "./packages/"
let testDir = "./test/"
let deployDir = "./deploy/"
let docsDir = "./docs/" 
let targetPlatformDir = getTargetPlatformDir "4.0.30139"

// params
let target = getBuildParamOrDefault "target" "All"

// tools
let fakePath = "./packages/FAKE.1.56.7/tools"
let nunitPath = "./packages/NUnit.2.5.9.10348/Tools"

// files
let appReferences =
    !+ "./src/lib/**/*.fsproj" 
        |> Scan

let testReferences =
    !+ "./src/tests/**/*.fsproj"
        |> Scan

let filesToZip =
    !+ (buildDir + "/**/*.*")     
        -- "*.zip"
        |> Scan      

// targets
Target "Clean" (fun _ ->
    CleanDirs [buildDir; testDir; deployDir; docsDir]
)

Target "BuildApp" (fun _ ->
    if isLocalBuild then
      Git.Submodule.init "" ""

    AssemblyInfo (fun p -> 
        {p with
           CodeLanguage = FSharp
           AssemblyVersion = buildVersion
           AssemblyTitle = projectName
           AssemblyDescription = projectDescription
           Guid = "5017411A-CF26-4E1A-85D6-1C49470C5996"
           OutputFileName = "./src/lib/Frank/AssemblyInfo.fs"})

    MSBuildRelease buildDir "Build" appReferences
        |> Log "AppBuild-Output: "
)

Target "BuildTest" (fun _ -> 
    MSBuildDebug testDir "Build" testReferences
        |> Log "TestBuild-Output: "
)

Target "Test" (fun _ ->
    !+ (testDir + "/*.Tests.dll")
        |> Scan
        |> NUnit (fun p -> 
            {p with 
               ToolPath = nunitPath; 
               DisableShadowCopy = true; 
               OutputFile = nunitPath + "TestResults.xml" }) 
)

Target "GenerateDocumentation" (fun _ ->
    !+ (buildDir + "Frank.dll")      
        |> Scan
        |> Docu (fun p ->
            {p with
               ToolPath = fakePath + "/docu.exe"
               TemplatesPath = "./lib/templates"
               OutputPath = docsDir })
)

Target "CopyLicense" (fun _ ->
    [ "LICENSE.txt" ] |> CopyTo buildDir
)

Target "ZipDocumentation" (fun _ ->    
    !+ (docsDir + "/**/*.*")
        |> Scan
        |> Zip docsDir (deployDir + sprintf "Documentation-%s.zip" version)
)

Target "Deploy" (fun _ ->    
    !+ (buildDir + "/**/*.*")
        -- "*.zip"
        |> Scan
        |> Zip buildDir (deployDir + sprintf "%s-%s.zip" projectName version)
)

Target "All" DoNothing

// Build order
"Clean"
  ==> "BuildApp" <=> "BuildTest" <=> "CopyLicense"
  ==> "Test" <=> "GenerateDocumentation"
  ==> "ZipDocumentation"
  ==> "Deploy"

"All" <== ["Deploy"]

// Start build
Run target
