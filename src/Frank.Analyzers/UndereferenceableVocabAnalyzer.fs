module Frank.Analyzers.UndereferenceableVocabAnalyzer

open System.IO
open FSharp.Analyzers.SDK
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabCheck

[<Literal>]
let Code = "FRANK002"

[<Literal>]
let private LockFileName = ".frank/semantic-mappings.lock.json"

[<Literal>]
let private WalkUpCap = 15

// ── Lock walk-up (bounded per Holzmann #10) ──────────────────────────────────

let private findLockFile (startDir: string) : string option =
    let rec walk (dir: string) (depth: int) =
        if depth > WalkUpCap then
            None // cap hit: emit no diagnostic, never throw
        else
            let candidate = Path.Combine(dir, LockFileName)

            if File.Exists candidate then
                Some candidate
            else
                let parent = Directory.GetParent dir

                if isNull parent then
                    None
                else
                    walk parent.FullName (depth + 1)

    walk startDir 0

// ── Diagnostic creation ───────────────────────────────────────────────────────

let private makeMessage (prefix: string) : Message =
    { Type = "Undereferenceable vocabulary"
      Message =
        $"Vocabulary '{prefix}' is neither dereferenceable (not in lock's fetched Vocabularies) nor routed (no resource covers its namespace path). Publish the vocabulary or add a route serving it."
      Code = Code
      Severity = Severity.Warning
      Range = Range.range0
      Fixes = [] }

// ── Analysis ─────────────────────────────────────────────────────────────────

/// Analyze a parse tree given an optional preloaded lock.
/// None lock → no diagnostics (cap hit or lock not found).
let analyzeWithLock (lock: LockFile option) (parseTree: ParsedInput) : Message list =
    match lock with
    | None -> []
    | Some lf ->
        let routes = extractRoutes parseTree
        let referencedNs = lf.DeclaredPrefixes |> Map.toList |> List.map fst
        checkUndereferenceableVocab lf routes referencedNs |> List.map makeMessage

[<Literal>]
let name = "UndereferenceableVocabAnalyzer"

[<Literal>]
let shortDescription =
    "Detects vocabulary namespaces that are neither dereferenceable nor routed"

[<Literal>]
let helpUri = "https://github.com/frank-fs/frank/issues/378"

let private loadLock (fileName: string) : LockFile option =
    let dir = Path.GetDirectoryName fileName

    findLockFile dir
    |> Option.bind (fun path ->
        match LockFile.read path with
        | Ok lf -> Some lf
        | Error _ -> None)

[<EditorAnalyzer(name, shortDescription, helpUri)>]
let editorAnalyzer: Analyzer<EditorContext> =
    fun ctx -> async { return analyzeWithLock (loadLock ctx.FileName) ctx.ParseFileResults.ParseTree }

[<CliAnalyzer(name, shortDescription, helpUri)>]
let cliAnalyzer: Analyzer<CliContext> =
    fun ctx -> async { return analyzeWithLock (loadLock ctx.FileName) ctx.ParseFileResults.ParseTree }
