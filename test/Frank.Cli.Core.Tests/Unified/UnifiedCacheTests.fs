module Frank.Cli.Core.Tests.Unified.UnifiedCacheTests

open System
open System.IO
open Expecto
open Frank.Cli.Core.Unified

/// Create a temporary project directory with some .fs files and a .fsproj.
let private setupTempProject () =
    let dir = Path.Combine(Path.GetTempPath(), $"frank-cache-test-{Guid.NewGuid():N}")
    Directory.CreateDirectory(dir) |> ignore

    File.WriteAllText(
        Path.Combine(dir, "Program.fs"),
        "module Program\n\n[<EntryPoint>]\nlet main _ = 0\n")

    File.WriteAllText(
        Path.Combine(dir, "Types.fs"),
        "namespace MyApp\n\ntype GameState = XTurn | OTurn\n")

    File.WriteAllText(
        Path.Combine(dir, "Test.fsproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>")

    dir

/// Create a sample state with a given source hash.
let private createSampleState (sourceHash: string) : UnifiedExtractionState =
    { Resources = []
      SourceHash = sourceHash
      BaseUri = "https://example.com/"
      Vocabularies = [ "schema.org" ]
      ExtractedAt = DateTimeOffset(2026, 3, 19, 0, 0, 0, TimeSpan.Zero)
      ToolVersion = UnifiedCache.currentToolVersion }

/// Cleanup a temp directory.
let private cleanup (dir: string) =
    try Directory.Delete(dir, true)
    with _ -> ()

[<Tests>]
let unifiedCacheTests =
    testList
        "UnifiedCache"
        [ testList
              "MessagePack roundtrip"
              [ testCase "roundtrips UnifiedExtractionState through save/load"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let state = createSampleState "abc123"
                        let path = UnifiedCache.cachePath dir

                        let saveResult = UnifiedCache.save path state
                        Expect.isOk saveResult "save should succeed"

                        let loadResult = UnifiedCache.load path
                        Expect.isOk loadResult "load should succeed"

                        match loadResult with
                        | Ok(Some loaded) ->
                            Expect.equal loaded.SourceHash state.SourceHash "SourceHash roundtrips"
                            Expect.equal loaded.BaseUri state.BaseUri "BaseUri roundtrips"
                            Expect.equal loaded.Vocabularies state.Vocabularies "Vocabularies roundtrip"
                            Expect.equal loaded.ToolVersion state.ToolVersion "ToolVersion roundtrips"
                            Expect.equal loaded.Resources state.Resources "Resources roundtrip"
                        | Ok None -> failtest "Expected Some state, got None"
                        | Error msg -> failtest $"Expected Ok, got Error: {msg}"
                    finally
                        cleanup dir

                testCase "load returns None for missing file"
                <| fun _ ->
                    let path = Path.Combine(Path.GetTempPath(), "nonexistent", "unified-state.bin")
                    let result = UnifiedCache.load path
                    Expect.equal result (Ok None) "Missing file returns Ok None"

                testCase "save creates directory if needed"
                <| fun _ ->
                    let dir = Path.Combine(Path.GetTempPath(), $"frank-cache-dir-{Guid.NewGuid():N}")

                    try
                        let state = createSampleState "abc123"
                        let path = Path.Combine(dir, "obj", "frank-cli", "unified-state.bin")
                        let result = UnifiedCache.save path state
                        Expect.isOk result "save should create directories and succeed"
                        Expect.isTrue (File.Exists(path)) "File should exist after save"
                    finally
                        cleanup dir

                testCase "load returns Error for corrupt data"
                <| fun _ ->
                    let dir = Path.Combine(Path.GetTempPath(), $"frank-cache-corrupt-{Guid.NewGuid():N}")
                    Directory.CreateDirectory(dir) |> ignore

                    try
                        let path = Path.Combine(dir, "unified-state.bin")
                        File.WriteAllBytes(path, [| 0xFFuy; 0xFEuy; 0x00uy |])
                        let result = UnifiedCache.load path
                        Expect.isError result "Corrupt data should return Error"
                    finally
                        cleanup dir ]

          testList
              "computeSourceHash"
              [ testCase "hash is deterministic"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let hash1 = UnifiedCache.computeSourceHash dir
                        let hash2 = UnifiedCache.computeSourceHash dir
                        Expect.equal hash1 hash2 "Same files should produce same hash"
                        Expect.isNonEmpty hash1 "Hash should not be empty"
                    finally
                        cleanup dir

                testCase "hash changes when source file is modified"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let hash1 = UnifiedCache.computeSourceHash dir
                        // Modify a source file
                        File.AppendAllText(Path.Combine(dir, "Program.fs"), "\nlet x = 42\n")
                        let hash2 = UnifiedCache.computeSourceHash dir
                        Expect.notEqual hash1 hash2 "Modified file should change hash"
                    finally
                        cleanup dir

                testCase "hash changes when new source file is added"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let hash1 = UnifiedCache.computeSourceHash dir
                        // Add a new source file
                        File.WriteAllText(
                            Path.Combine(dir, "NewModule.fs"),
                            "module NewModule\n\nlet value = 1\n")
                        let hash2 = UnifiedCache.computeSourceHash dir
                        Expect.notEqual hash1 hash2 "Added file should change hash"
                    finally
                        cleanup dir

                testCase "hash changes when fsproj is modified"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let hash1 = UnifiedCache.computeSourceHash dir
                        // Modify the .fsproj file
                        File.AppendAllText(Path.Combine(dir, "Test.fsproj"), "<!-- modified -->")
                        let hash2 = UnifiedCache.computeSourceHash dir
                        Expect.notEqual hash1 hash2 "Modified fsproj should change hash"
                    finally
                        cleanup dir

                testCase "hash excludes obj and bin directories"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let hash1 = UnifiedCache.computeSourceHash dir
                        // Add files in obj/ and bin/
                        let objDir = Path.Combine(dir, "obj")
                        Directory.CreateDirectory(objDir) |> ignore
                        File.WriteAllText(Path.Combine(objDir, "Generated.fs"), "// generated")
                        let binDir = Path.Combine(dir, "bin")
                        Directory.CreateDirectory(binDir) |> ignore
                        File.WriteAllText(Path.Combine(binDir, "Output.fs"), "// output")
                        let hash2 = UnifiedCache.computeSourceHash dir
                        Expect.equal hash1 hash2 "obj/ and bin/ files should not affect hash"
                    finally
                        cleanup dir ]

          testList
              "staleness detection"
              [ testCase "fresh cache returns Ok"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let sourceHash = UnifiedCache.computeSourceHash dir
                        let state = createSampleState sourceHash
                        let path = UnifiedCache.cachePath dir
                        UnifiedCache.save path state |> ignore

                        let result = UnifiedCache.tryLoadFresh dir false
                        Expect.isOk result "Cache with matching hash should be fresh"

                        match result with
                        | Ok loaded ->
                            Expect.equal loaded.SourceHash sourceHash "Source hash matches"
                        | Error _ -> failtest "Expected Ok"
                    finally
                        cleanup dir

                testCase "stale cache (modified source) returns SourceHashMismatch"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let sourceHash = UnifiedCache.computeSourceHash dir
                        let state = createSampleState sourceHash
                        let path = UnifiedCache.cachePath dir
                        UnifiedCache.save path state |> ignore

                        // Modify a source file
                        File.AppendAllText(Path.Combine(dir, "Types.fs"), "\ntype Extra = { X: int }\n")

                        let result = UnifiedCache.tryLoadFresh dir false

                        match result with
                        | Error(UnifiedCache.SourceHashMismatch _) -> ()
                        | other -> failtest $"Expected SourceHashMismatch, got {other}"
                    finally
                        cleanup dir

                testCase "missing cache returns CacheNotFound"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let result = UnifiedCache.tryLoadFresh dir false

                        match result with
                        | Error UnifiedCache.CacheNotFound -> ()
                        | other -> failtest $"Expected CacheNotFound, got {other}"
                    finally
                        cleanup dir

                testCase "force flag bypasses valid cache"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let sourceHash = UnifiedCache.computeSourceHash dir
                        let state = createSampleState sourceHash
                        let path = UnifiedCache.cachePath dir
                        UnifiedCache.save path state |> ignore

                        // Without force: cache is fresh
                        let result = UnifiedCache.tryLoadFresh dir false
                        Expect.isOk result "Should load from cache without force"

                        // With force: cache is bypassed
                        let forced = UnifiedCache.tryLoadFresh dir true

                        match forced with
                        | Error UnifiedCache.ForcedRefresh -> ()
                        | other -> failtest $"Expected ForcedRefresh, got {other}"
                    finally
                        cleanup dir

                testCase "tool version mismatch returns ToolVersionMismatch"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let sourceHash = UnifiedCache.computeSourceHash dir
                        let state =
                            { createSampleState sourceHash with
                                ToolVersion = "0.0.1-old" }
                        let path = UnifiedCache.cachePath dir
                        UnifiedCache.save path state |> ignore

                        let result = UnifiedCache.tryLoadFresh dir false

                        match result with
                        | Error(UnifiedCache.ToolVersionMismatch(cached, current)) ->
                            Expect.equal cached "0.0.1-old" "Cached version"
                            Expect.equal current UnifiedCache.currentToolVersion "Current version"
                        | other -> failtest $"Expected ToolVersionMismatch, got {other}"
                    finally
                        cleanup dir

                testCase "corrupt cache returns CacheCorrupt"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let path = UnifiedCache.cachePath dir
                        let cacheDir = Path.GetDirectoryName(path)
                        Directory.CreateDirectory(cacheDir) |> ignore
                        File.WriteAllBytes(path, [| 0xFFuy; 0xFEuy; 0x00uy |])

                        let result = UnifiedCache.tryLoadFresh dir false

                        match result with
                        | Error(UnifiedCache.CacheCorrupt _) -> ()
                        | other -> failtest $"Expected CacheCorrupt, got {other}"
                    finally
                        cleanup dir ]

          testList
              "saveExtractionState"
              [ testCase "saves and can be loaded back"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let resources: UnifiedResource list = []
                        let baseUri = "https://example.com/"
                        let vocabs = [ "schema.org" ]

                        let result = UnifiedCache.saveExtractionState dir resources baseUri vocabs
                        Expect.isOk result "saveExtractionState should succeed"

                        match result with
                        | Ok state ->
                            Expect.equal state.BaseUri baseUri "BaseUri preserved"
                            Expect.equal state.Vocabularies vocabs "Vocabularies preserved"
                            Expect.equal state.ToolVersion UnifiedCache.currentToolVersion "ToolVersion set"
                            Expect.isNonEmpty state.SourceHash "SourceHash computed"

                            // Verify it can be loaded fresh
                            let loaded = UnifiedCache.tryLoadFresh dir false
                            Expect.isOk loaded "Saved state should load as fresh"
                        | Error msg -> failtest $"Expected Ok, got Error: {msg}"
                    finally
                        cleanup dir

                testCase "saveExtractionState with resources roundtrips"
                <| fun _ ->
                    let dir = setupTempProject ()

                    try
                        let resources: UnifiedResource list =
                            [ { RouteTemplate = "/games/{gameId}"
                                ResourceSlug = "games"
                                TypeInfo = []
                                Statechart = None
                                HttpCapabilities =
                                    [ { Method = "GET"
                                        StateKey = None
                                        LinkRelation = "self"
                                        IsSafe = true } ]
                                DerivedFields = UnifiedModel.emptyDerivedFields } ]

                        let result =
                            UnifiedCache.saveExtractionState dir resources "https://example.com/" [ "schema.org" ]

                        Expect.isOk result "saveExtractionState should succeed"

                        match result with
                        | Ok state ->
                            Expect.equal state.Resources.Length 1 "One resource"
                            Expect.equal state.Resources.[0].RouteTemplate "/games/{gameId}" "Route preserved"
                        | Error msg -> failtest $"Expected Ok, got Error: {msg}"
                    finally
                        cleanup dir ] ]
