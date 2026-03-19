module Frank.Cli.Core.Unified.ExtractionStateProjector

open System
open System.IO
open Frank.Statecharts.Unified
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction
open Frank.Cli.Core.State

/// Collect all AnalyzedTypes from unified resources, deduplicated by FullName.
let private collectAllTypes (unified: UnifiedExtractionState) : AnalyzedType list =
    unified.Resources
    |> List.collect (fun r -> r.TypeInfo)
    |> List.distinctBy (fun t -> t.FullName)

/// Build a TypeMapper.MappingConfig from unified state metadata.
let private buildMappingConfig (unified: UnifiedExtractionState) : TypeMapper.MappingConfig =
    { BaseUri = Uri(unified.BaseUri)
      Vocabularies = unified.Vocabularies }

/// Build the SourceMap from unified resource location data.
let private buildSourceMap (unified: UnifiedExtractionState) : Map<string, SourceLocation> =
    let baseUri = unified.BaseUri.TrimEnd('/')

    unified.Resources
    |> List.collect (fun resource ->
        resource.TypeInfo
        |> List.collect (fun analyzedType ->
            let typeEntry =
                match analyzedType.SourceLocation with
                | Some loc ->
                    [ $"%s{baseUri}#%s{analyzedType.ShortName}",
                      { File = loc.File; Line = loc.Line; Column = loc.Column } ]
                | None -> []

            let fieldEntries =
                match analyzedType.Kind with
                | Record fields ->
                    fields
                    |> List.choose (fun field ->
                        match analyzedType.SourceLocation with
                        | Some loc ->
                            Some(
                                $"%s{baseUri}#%s{analyzedType.ShortName}/%s{field.Name}",
                                { File = loc.File; Line = loc.Line; Column = loc.Column }
                            )
                        | None -> None)
                | _ -> []

            typeEntry @ fieldEntries))
    |> Map.ofList

/// Build ExtractionMetadata from unified state.
let private buildMetadata (unified: UnifiedExtractionState) : ExtractionMetadata =
    { Timestamp = unified.ExtractedAt
      SourceHash = unified.SourceHash
      ToolVersion = unified.ToolVersion
      BaseUri = Uri(unified.BaseUri)
      Vocabularies = unified.Vocabularies }

/// Project a UnifiedExtractionState into the legacy ExtractionState format
/// used by semantic subcommands (clarify, validate, compile, diff).
let toExtractionState (unified: UnifiedExtractionState) : ExtractionState =
    let config = buildMappingConfig unified
    let allTypes = collectAllTypes unified
    let ontology = TypeMapper.mapTypes config allTypes
    let shapes = ShapeGenerator.generateShapes config allTypes

    { Ontology = ontology
      Shapes = shapes
      SourceMap = buildSourceMap unified
      Clarifications = Map.empty
      Metadata = buildMetadata unified
      UnmappedTypes = [] }

/// Shared loading logic for semantic subcommands.
/// Tries unified state first, falls back to legacy state.json with migration message.
module UnifiedStateLoader =

    let private unifiedStatePath (projectDir: string) : string =
        Path.Combine(projectDir, "obj", "frank-cli", "descriptors.bin")

    /// Load extraction state from unified binary or detect old format.
    let loadExtractionState (projectDir: string) : Result<ExtractionState, string> =
        let unifiedPath = unifiedStatePath projectDir
        let legacyPath = ExtractionState.defaultStatePath projectDir

        if File.Exists unifiedPath then
            try
                let bytes = File.ReadAllBytes unifiedPath

                let unified =
                    MessagePack.MessagePackSerializer.Deserialize<UnifiedExtractionState>(
                        bytes,
                        MessagePack.Resolvers.ContractlessStandardResolverAllowPrivate.Options
                    )

                Ok(toExtractionState unified)
            with ex ->
                // Unified file exists but is corrupt -- check for legacy fallback
                if File.Exists legacyPath then
                    Error
                        $"Unified state file is unreadable (%s{ex.Message}). Please re-extract: frank-cli semantic extract --project <fsproj>"
                else
                    Error $"Unified state file is unreadable: %s{ex.Message}"
        elif File.Exists legacyPath then
            // Legacy state.json detected -- try to load it for backward compatibility,
            // but also suggest re-extraction for the unified pipeline
            match ExtractionState.load legacyPath with
            | Ok state ->
                // Legacy format still works, but warn about migration
                Ok state
            | Error e ->
                Error $"Failed to load state: %s{e}"
        else
            Error
                "No extraction state found. Run: frank-cli semantic extract --project <fsproj> --base-uri <URI>"
