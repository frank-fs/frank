module Frank.Analyzers.HrefVarAnalyzer

open FSharp.Analyzers.SDK
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// Every `hrefVar "name" <uri-expr>` call captured from inside a
/// `resource "<template>" { }` body, with the range of its declaration.
/// Recurses through `let` bindings inside the CE body so a hrefVar
/// declaration after an intervening `let` isn't silently dropped.
val collectHrefVars: bodyExpr: SynExpr -> (string * range) list

/// Recognizes a `resource "<template>" { <body> }` call site (string-literal
/// template only -- NOT `resource <identifier> { }`, the already-built-value
/// form used inside `webHost { }`). Returns the template text, its range,
/// and the CE body to scan for hrefVar declarations.
val tryResourceLiteral: expr: SynExpr -> (string * range * SynExpr) option

/// Message for a route template variable with no hrefVar declaration.
val createMissingMessage: varName: string -> resourceRange: range -> Message

/// Message for a declared hrefVar with no matching route template variable.
val createExtraMessage: varName: string -> declRange: range -> Message

/// Analyze a parsed F# file for hrefVar / route template mismatches.
val analyzeFile: parseTree: ParsedInput -> Message list

[<Literal>]
val name: string = "HrefVarAnalyzer"

[<Literal>]
val shortDescription: string =
    "Detects hrefVar declarations that don't match the resource's route template variables (FRANK003)"

[<Literal>]
val helpUri: string = "https://github.com/frank-fs/frank/issues/474"

/// Editor analyzer for IDE integration (Ionide, Visual Studio, Rider)
[<EditorAnalyzer(name, shortDescription, helpUri)>]
val editorAnalyzer: Analyzer<EditorContext>

/// CLI analyzer for command-line and CI/CD usage
[<CliAnalyzer(name, shortDescription, helpUri)>]
val cliAnalyzer: Analyzer<CliContext>
