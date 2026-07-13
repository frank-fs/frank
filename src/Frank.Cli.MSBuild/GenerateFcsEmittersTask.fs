namespace Frank.Cli.MSBuild

open System
open System.IO
open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

/// Consolidated MSBuild task: runs ONE FCS ParseAndCheckProject over the shared sources
/// and calls LinkedData / Semantic / Validation / Provenance emitters in sequence.
///
/// HasLinkedData/HasValidation/HasProvenance gate their respective emitters.
/// Semantics emits unconditionally whenever the target runs (lock-only, no package gate).
/// HasSemantic is retained for API compatibility but is not used as a gate.
/// Discovery is NOT handled here — it uses its own GenerateDiscoveryTask (lock-only, no FCS).
///
/// FcsPassCount (output property) is always 1 after a successful run.
/// Use it in unit tests to assert exactly one ParseAndCheckProject call.
type GenerateFcsEmittersTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    [<Required>]
    member val OutputPath: string = "" with get, set

    [<Required>]
    member val SourceFiles: ITaskItem[] = [||] with get, set

    [<Required>]
    member val AssemblyRefs: ITaskItem[] = [||] with get, set

    member val VocabularyBinding: string = "registry" with get, set

    member val HasLinkedData: bool = false with get, set
    /// Retained for API compatibility. Semantics emits unconditionally; this flag is ignored.
    member val HasSemantic: bool = false with get, set
    member val HasValidation: bool = false with get, set
    member val HasProvenance: bool = false with get, set

    member val LinkedDataModuleName: string = "" with get, set
    member val SemanticsModuleName: string = "" with get, set
    member val ValidationModuleName: string = "" with get, set
    member val ProvenanceModuleName: string = "" with get, set

    [<Output>]
    member val GeneratedLinkedDataFile: string = "" with get, set

    [<Output>]
    member val GeneratedSemanticsFile: string = "" with get, set

    [<Output>]
    member val GeneratedValidationFile: string = "" with get, set

    [<Output>]
    member val GeneratedProvenanceFile: string = "" with get, set

    /// Counts ParseAndCheckProject invocations. Always 1 after a successful run.
    /// Exposed as an output property for unit-test counting seam (AC1 #386).
    [<Output>]
    member val FcsPassCount: int = 0 with get, set

    override this.Execute() =
        if String.IsNullOrWhiteSpace this.LockFilePath then
            this.Log.LogError("GenerateFcsEmittersTask: LockFilePath must not be empty.")
            false
        elif String.IsNullOrWhiteSpace this.OutputPath then
            this.Log.LogError("GenerateFcsEmittersTask: OutputPath must not be empty.")
            false
        elif this.SourceFiles.Length = 0 then
            this.Log.LogError("GenerateFcsEmittersTask: SourceFiles must not be empty.")
            false
        elif this.AssemblyRefs.Length = 0 then
            this.Log.LogError("GenerateFcsEmittersTask: AssemblyRefs must not be empty.")
            false
        else
            this.RunGenerate()

    member private this.RunGenerate() =
        match LockFile.read this.LockFilePath with
        | Error msg ->
            this.Log.LogError($"GenerateFcsEmittersTask: could not read lock file: {msg}")
            false
        | Ok lock ->
            let refs = this.AssemblyRefs |> Array.map (fun i -> i.ItemSpec) |> Array.toList
            let sources = this.SourceFiles |> Array.map (fun i -> i.ItemSpec) |> Array.toList
            let binding = this.VocabularyBinding

            match VocabularyEvaluator.typecheckShared refs sources with
            | Error msg ->
                this.Log.LogError($"GenerateFcsEmittersTask: FCS typecheck failed: {msg}")
                false
            | Ok check ->
                this.FcsPassCount <- 1
                this.Log.LogMessage(MessageImportance.High, $"FRANK_FCS_PASS_COUNT={this.FcsPassCount}")
                this.RunEmitters check lock binding

    member private this.PrepareEmitterInputs
        (check: VocabularyEvaluator.SharedCheck)
        (binding: string)
        : Result<VocabularyRegistry * TypeInfo list, string> =
        match VocabularyEvaluator.evalImplFiles check.ImplFiles binding with
        | Error msg -> Error msg
        | Ok registry ->
            let typeInfos =
                if this.HasValidation then
                    Extractor.extractTypeInfosFromEntities check.SignatureEntities check.ProjectFiles
                else
                    []

            Ok(registry, typeInfos)

    member private this.RunEmitters (check: VocabularyEvaluator.SharedCheck) (lock: LockFile) (binding: string) =
        match this.PrepareEmitterInputs check binding with
        | Error msg ->
            this.Log.LogError($"GenerateFcsEmittersTask: vocabulary evaluation failed: {msg}")
            false
        | Ok(registry, typeInfos) ->
            let r1 =
                this.RunEmitter
                    this.HasLinkedData
                    "GeneratedLinkedData.fs"
                    (fun () -> LinkedDataEmitter.emit this.LinkedDataModuleName registry lock)
                    (fun p -> this.GeneratedLinkedDataFile <- p)

            let r2 =
                this.RunEmitter
                    true
                    "GeneratedSemantics.fs"
                    (fun () -> SemanticModelEmitter.emit this.SemanticsModuleName registry lock)
                    (fun p -> this.GeneratedSemanticsFile <- p)

            let r3 = this.EmitValidation registry lock typeInfos

            let r4 =
                this.RunEmitter
                    this.HasProvenance
                    "GeneratedProvenance.fs"
                    (fun () -> ProvenanceEmitter.emit this.ProvenanceModuleName registry lock)
                    (fun p -> this.GeneratedProvenanceFile <- p)

            r1 && r2 && r3 && r4

    /// Shared emitter runner: gate on enabled flag, call emitFn, write output.
    member private this.RunEmitter
        (enabled: bool)
        (fileName: string)
        (emitFn: unit -> Result<string, string>)
        (setPath: string -> unit)
        : bool =
        if not enabled then
            true
        else
            match emitFn () with
            | Error msg ->
                this.Log.LogError($"GenerateFcsEmittersTask: {fileName} codegen failed: {msg}")
                false
            | Ok source -> this.WriteOutput source fileName setPath

    member private this.EmitValidation (registry: VocabularyRegistry) (lock: LockFile) (typeInfos: TypeInfo list) =
        if not this.HasValidation then
            true
        else
            let typesByName = typeInfos |> List.map (fun ti -> ti.FullName, ti) |> Map.ofList

            match ValidationEmitter.emit this.ValidationModuleName registry lock typesByName with
            | Error msg ->
                this.Log.LogError($"GenerateFcsEmittersTask: GeneratedValidation.fs codegen failed: {msg}")
                false
            | Ok source -> this.WriteOutput source "GeneratedValidation.fs" (fun p -> this.GeneratedValidationFile <- p)

    member private this.WriteOutput (source: string) (fileName: string) (setPath: string -> unit) =
        let outPath = Path.Combine(this.OutputPath, fileName)

        try
            Directory.CreateDirectory(this.OutputPath) |> ignore
            File.WriteAllText(outPath, source)
            setPath outPath
            true
        with ex ->
            this.Log.LogError($"GenerateFcsEmittersTask: could not write '{outPath}': {ex.Message}")
            false
