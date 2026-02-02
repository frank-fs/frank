module TestConfiguration

open System
open System.IO

/// Test configuration loaded from environment variables
type Config =
    { SampleName: string
      BaseUrl: string
      TimeoutMs: int
      AvailableSamples: string list }

/// Discovers available Frank.Datastar.* sample folders
let discoverSamples (sampleRoot: string) : string list =
    if not (Directory.Exists sampleRoot) then
        []
    else
        Directory.GetDirectories(sampleRoot)
        |> Array.filter (fun d -> Path.GetFileName(d).StartsWith("Frank.Datastar."))
        |> Array.filter (fun d -> Path.GetFileName(d) <> "Frank.Datastar.Tests")
        |> Array.map Path.GetFileName
        |> Array.sort
        |> Array.toList

/// Generates help message listing available samples
let generateHelpMessage (samples: string list) : string =
    let sampleList =
        if samples.IsEmpty then
            "  (no samples found)"
        else
            samples |> List.map (fun s -> $"  - {s}") |> String.concat Environment.NewLine

    $"""
DATASTAR_SAMPLE environment variable is required.

Usage:
  DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/

Available samples:
{sampleList}

Optional environment variables:
  DATASTAR_BASE_URL   - Base URL of running sample (default: http://localhost:5000)
  DATASTAR_TIMEOUT_MS - Timeout for SSE updates in ms (default: 5000)
"""

/// Generates error message for invalid sample name
let generateInvalidSampleMessage (sampleName: string) (samples: string list) : string =
    let sampleList =
        if samples.IsEmpty then
            "  (no samples found)"
        else
            samples |> List.map (fun s -> $"  - {s}") |> String.concat Environment.NewLine

    $"""
Sample '{sampleName}' not found.

Available samples:
{sampleList}
"""

/// Generates error message for invalid sample pattern
let generatePatternErrorMessage (sampleName: string) : string =
    $"""
Invalid sample name: '{sampleName}'

Sample name must start with 'Frank.Datastar.' (e.g., Frank.Datastar.Basic)
"""

/// Loads configuration from environment variables.
/// Fails fast with helpful message if configuration is invalid.
let loadConfig () : Config =
    // Determine sample root path (relative to test project location)
    let assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location
    let assemblyDir = Path.GetDirectoryName(assemblyLocation)

    // Navigate from bin/Debug/net10.0 up to sample/ directory
    // Test project is at sample/Frank.Datastar.Tests/
    // Assembly is at sample/Frank.Datastar.Tests/bin/Debug/net10.0/
    let sampleRoot =
        Path.Combine(assemblyDir, "..", "..", "..", "..")
        |> Path.GetFullPath

    let availableSamples = discoverSamples sampleRoot

    // Get sample name from environment
    let sampleName =
        Environment.GetEnvironmentVariable("DATASTAR_SAMPLE")
        |> Option.ofObj
        |> Option.map (fun s -> s.Trim())
        |> Option.filter (fun s -> not (String.IsNullOrEmpty(s)))

    match sampleName with
    | None ->
        failwith (generateHelpMessage availableSamples)
    | Some name when not (name.StartsWith("Frank.Datastar.")) ->
        failwith (generatePatternErrorMessage name)
    | Some name when not (availableSamples |> List.contains name) ->
        failwith (generateInvalidSampleMessage name availableSamples)
    | Some name ->
        let baseUrl =
            Environment.GetEnvironmentVariable("DATASTAR_BASE_URL")
            |> Option.ofObj
            |> Option.filter (fun s -> not (String.IsNullOrEmpty(s)))
            |> Option.defaultValue "http://localhost:5000"

        let timeoutMs =
            Environment.GetEnvironmentVariable("DATASTAR_TIMEOUT_MS")
            |> Option.ofObj
            |> Option.bind (fun s ->
                match Int32.TryParse(s) with
                | true, v when v > 0 -> Some v
                | _ -> None)
            |> Option.defaultValue 5000

        { SampleName = name
          BaseUrl = baseUrl
          TimeoutMs = timeoutMs
          AvailableSamples = availableSamples }

/// Global configuration instance (loaded once at test startup)
let config = lazy (loadConfig ())

/// Gets the loaded configuration
let getConfig () = config.Value
