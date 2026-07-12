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
/// Per-package gating: set HasLinkedData/HasSemantic/HasValidation/HasProvenance to
/// emit only the packages referenced by the consuming project.
/// Discovery is NOT handled here — it uses its own GenerateDiscoveryTask (lock-only, no FCS).
///
/// FcsPassCount (output property) is 1 after a successful run, 0 when all HasX are false.
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

    /// Counts ParseAndCheckProject invocations. 1 after a normal run; 0 when all HasX=false.
    /// Exposed as an output property for unit-test counting seam (AC1 #386).
    [<Output>]
    member val FcsPassCount: int = 0 with get, set

    override this.Execute() =
        let anyEnabled =
            this.HasLinkedData
            || this.HasSemantic
            || this.HasValidation
            || this.HasProvenance

        if not anyEnabled then
            true
        elif String.IsNullOrWhiteSpace this.LockFilePath then
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
                this.FcsPassCount <- this.FcsPassCount + 1
                this.Log.LogMessage(MessageImportance.High, $"FRANK_FCS_PASS_COUNT={this.FcsPassCount}")
                this.RunEmitters check lock binding

    member private this.RunEmitters (check: VocabularyEvaluator.SharedCheck) (lock: LockFile) (binding: string) =
        match VocabularyEvaluator.evalImplFiles check.ImplFiles binding with
        | Error msg ->
            this.Log.LogError($"GenerateFcsEmittersTask: vocabulary evaluation failed: {msg}")
            false
        | Ok registry ->
            let typeInfos =
                if this.HasValidation then
                    Extractor.extractTypeInfosFromEntities check.SignatureEntities check.ProjectFiles
                else
                    []

            let r1 = this.EmitLinkedData registry lock
            let r2 = this.EmitSemantics registry lock
            let r3 = this.EmitValidation registry lock typeInfos
            let r4 = this.EmitProvenance registry lock
            r1 && r2 && r3 && r4

    member private this.EmitLinkedData (registry: VocabularyRegistry) (lock: LockFile) =
        if not this.HasLinkedData then
            true
        else
            match LinkedDataEmitter.emit this.LinkedDataModuleName registry lock with
            | Error msg ->
                this.Log.LogError($"GenerateFcsEmittersTask: LinkedData codegen failed: {msg}")
                false
            | Ok source -> this.WriteOutput source "GeneratedLinkedData.fs" (fun p -> this.GeneratedLinkedDataFile <- p)

    member private this.EmitSemantics (registry: VocabularyRegistry) (lock: LockFile) =
        if not this.HasSemantic then
            true
        else
            match SemanticModelEmitter.emit this.SemanticsModuleName registry lock with
            | Error msg ->
                this.Log.LogError($"GenerateFcsEmittersTask: Semantics codegen failed: {msg}")
                false
            | Ok source -> this.WriteOutput source "GeneratedSemantics.fs" (fun p -> this.GeneratedSemanticsFile <- p)

    member private this.EmitValidation (registry: VocabularyRegistry) (lock: LockFile) (typeInfos: TypeInfo list) =
        if not this.HasValidation then
            true
        else
            let typesByName = typeInfos |> List.map (fun ti -> ti.FullName, ti) |> Map.ofList

            match ValidationEmitter.emit this.ValidationModuleName registry lock typesByName with
            | Error msg ->
                this.Log.LogError($"GenerateFcsEmittersTask: Validation codegen failed: {msg}")
                false
            | Ok source -> this.WriteOutput source "GeneratedValidation.fs" (fun p -> this.GeneratedValidationFile <- p)

    member private this.EmitProvenance (registry: VocabularyRegistry) (lock: LockFile) =
        if not this.HasProvenance then
            true
        else
            match ProvenanceEmitter.emit this.ProvenanceModuleName registry lock with
            | Error msg ->
                this.Log.LogError($"GenerateFcsEmittersTask: Provenance codegen failed: {msg}")
                false
            | Ok source -> this.WriteOutput source "GeneratedProvenance.fs" (fun p -> this.GeneratedProvenanceFile <- p)

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
