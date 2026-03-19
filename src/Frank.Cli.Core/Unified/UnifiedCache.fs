module Frank.Cli.Core.Unified.UnifiedCache

open System
open System.IO
open System.Security.Cryptography
open Frank.Affordances
open Frank.Statecharts.Unified
open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp

/// MessagePack serialization options for F# types.
/// FSharpResolver handles DUs, options, lists, maps.
/// ContractlessStandardResolver handles public records without attributes.
let private msgpackOptions =
    MessagePackSerializerOptions
        .Standard
        .WithResolver(
            CompositeResolver.Create(
                FSharpResolver.Instance,
                ContractlessStandardResolver.Instance))

/// Cache file name for the unified extraction state.
let cacheFileName = "unified-state.bin"

/// Compute the full cache path for a project directory.
let cachePath (projectDir: string) : string =
    Path.Combine(projectDir, "obj", "frank-cli", cacheFileName)

/// Serialize and write UnifiedExtractionState to binary cache file.
let save (path: string) (state: UnifiedExtractionState) : Result<unit, string> =
    try
        let dir = Path.GetDirectoryName(path)

        if not (Directory.Exists(dir)) then
            Directory.CreateDirectory(dir) |> ignore

        let bytes = MessagePackSerializer.Serialize(state, msgpackOptions)
        File.WriteAllBytes(path, bytes)
        Ok()
    with ex ->
        Error $"Failed to write cache: {ex.Message}"

/// Deserialize UnifiedExtractionState from binary cache file.
/// Returns None if file doesn't exist.
/// Returns Error if deserialization fails.
let load (path: string) : Result<UnifiedExtractionState option, string> =
    try
        if not (File.Exists(path)) then
            Ok None
        else
            let bytes = File.ReadAllBytes(path)
            let state = MessagePackSerializer.Deserialize<UnifiedExtractionState>(bytes, msgpackOptions)
            Ok(Some state)
    with ex ->
        Error $"Failed to read cache: {ex.Message}"

/// Compute a SHA256 hash of all .fs source files in a project directory.
/// Files are sorted by path to ensure deterministic ordering.
/// The hash covers file content, not timestamps (content-based staleness).
let computeSourceHash (projectDir: string) : string =
    use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)

    // Find all .fs files in the project directory (excluding obj/ and bin/)
    let sourceFiles =
        Directory.GetFiles(projectDir, "*.fs", SearchOption.AllDirectories)
        |> Array.filter (fun f ->
            let rel = Path.GetRelativePath(projectDir, f)
            not (rel.StartsWith("obj" + string Path.DirectorySeparatorChar))
            && not (rel.StartsWith("bin" + string Path.DirectorySeparatorChar))
            && not (rel.StartsWith("obj/"))
            && not (rel.StartsWith("bin/")))
        |> Array.sort // Deterministic ordering

    for file in sourceFiles do
        let content = File.ReadAllBytes(file)
        hash.AppendData(content)

    // Also hash the .fsproj file itself (compile order changes matter)
    let fsprojFiles = Directory.GetFiles(projectDir, "*.fsproj")

    for fsproj in fsprojFiles do
        let content = File.ReadAllBytes(fsproj)
        hash.AppendData(content)

    let hashBytes = hash.GetHashAndReset()
    Convert.ToHexString(hashBytes).ToLowerInvariant()

/// CLI version for cache compatibility checking.
/// Update this when UnifiedExtractionState schema changes.
let currentToolVersion = "7.3.0"

/// Reason why the cache is stale and re-extraction is needed.
type StalenessReason =
    | CacheNotFound
    | SourceHashMismatch of cached: string * current: string
    | ToolVersionMismatch of cached: string * current: string
    | ForcedRefresh
    | CacheCorrupt of message: string

/// Check whether the cache is fresh for the given project.
/// Returns Ok(state) if fresh, Error(reason) if stale/missing.
let checkStaleness
    (projectDir: string)
    (force: bool)
    (cached: Result<UnifiedExtractionState option, string>)
    : Result<UnifiedExtractionState, StalenessReason> =

    if force then
        Error ForcedRefresh
    else
        match cached with
        | Error msg -> Error(CacheCorrupt msg)
        | Ok None -> Error CacheNotFound
        | Ok(Some state) ->
            // Check tool version compatibility first (fail fast)
            if state.ToolVersion <> currentToolVersion then
                Error(ToolVersionMismatch(state.ToolVersion, currentToolVersion))
            else
                // Check source hash
                let currentHash = computeSourceHash projectDir
                if state.SourceHash <> currentHash then
                    Error(SourceHashMismatch(state.SourceHash, currentHash))
                else
                    Ok state

/// Try to load a fresh cached state.
/// Returns Ok(state) if cache is fresh, Error(reason) if stale/missing.
let tryLoadFresh (projectDir: string) (force: bool) : Result<UnifiedExtractionState, StalenessReason> =
    let cacheFile = cachePath projectDir
    let cached = load cacheFile
    checkStaleness projectDir force cached

/// Save unified extraction state to cache and return the state.
/// Creates the obj/frank-cli/ directory if needed.
let saveExtractionState
    (projectDir: string)
    (resources: UnifiedResource list)
    (baseUri: string)
    (vocabularies: string list)
    : Result<UnifiedExtractionState, string> =

    let sourceHash = computeSourceHash projectDir
    let state: UnifiedExtractionState =
        { Resources = resources
          SourceHash = sourceHash
          BaseUri = baseUri
          Vocabularies = vocabularies
          ExtractedAt = DateTimeOffset.UtcNow
          ToolVersion = currentToolVersion
          Profiles = ProjectedProfiles.empty }

    let cacheFile = cachePath projectDir
    match save cacheFile state with
    | Ok() -> Ok state
    | Error msg -> Error msg
