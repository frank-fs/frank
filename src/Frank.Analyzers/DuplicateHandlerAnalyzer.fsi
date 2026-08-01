module Frank.Analyzers.DuplicateHandlerAnalyzer

open FSharp.Analyzers.SDK
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// HTTP methods supported by Frank's ResourceBuilder
type HttpMethod =
    | GET
    | POST
    | PUT
    | DELETE
    | PATCH
    | HEAD
    | OPTIONS
    | CONNECT
    | TRACE

/// Set of HTTP method operation names (lowercase) from Frank's ResourceBuilder
val httpMethodOperations: Set<string>

/// Try to extract HTTP method from datastar operation with explicit method argument
val tryGetDatastarMethodFromArg: argExpr: SynExpr -> string option

/// Create a message for a duplicate HTTP handler
val createDuplicateMessage: methodName: string -> duplicateRange: range -> firstRange: range -> Message

/// Create a message for a duplicate `accepts` media-type registration inside one
/// `negotiate { }` block
val createDuplicateMediaTypeMessage: mediaType: string -> duplicateRange: range -> firstRange: range -> Message

/// Analyze a parsed F# file for duplicate HTTP handlers
val analyzeFile: parseTree: ParsedInput -> Message list

[<Literal>]
val name: string = "DuplicateHandlerAnalyzer"

[<Literal>]
val shortDescription: string = "Detects duplicate HTTP method handlers in Frank resource definitions"

[<Literal>]
val helpUri: string = "https://github.com/frank-fs/frank/issues/59"

/// Editor analyzer for IDE integration (Ionide, Visual Studio, Rider)
[<EditorAnalyzer(name, shortDescription, helpUri)>]
val editorAnalyzer: Analyzer<EditorContext>

/// CLI analyzer for command-line and CI/CD usage
[<CliAnalyzer(name, shortDescription, helpUri)>]
val cliAnalyzer: Analyzer<CliContext>
