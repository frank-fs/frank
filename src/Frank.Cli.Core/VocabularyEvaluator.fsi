module Frank.Cli.Core.VocabularyEvaluator

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open Frank.Semantic

/// Combined result of a single FCS project typecheck.
/// Carries both the typed implementation tree (for vocabulary CE walking)
/// and the signature entities (for type extraction).
/// Held in-process only — never serialized; valid for the lifetime of the task.
type SharedCheck =
    { ImplFiles: FSharpImplementationFileContents list
      SignatureEntities: FSharpEntity seq
      ProjectFiles: Set<string> }

/// Typecheck F# source files via FCS. Returns implementation file contents.
/// Exposed so callers can typecheck once and re-use implFiles across multiple evalImplFiles calls.
/// Not consumed by Frank.Cli/Frank.Cli.MSBuild today; tested directly via InternalsVisibleTo.
val internal typecheckSources:
    assemblyRefs: string list -> sourceFiles: string list -> Result<FSharpImplementationFileContents list, string>

/// Typecheck F# source files once and return a SharedCheck carrying both the
/// typed implementation tree (for vocabulary CE walking via evalImplFiles) and
/// the signature entities (for type extraction via Extractor.extractTypeInfosFromEntities).
/// This is the entry point for the consolidated FCS emitter task — one call serves all four emitters.
val typecheckShared: assemblyRefs: string list -> sourceFiles: string list -> Result<SharedCheck, string>

/// Walk the post-typecheck typed AST and evaluate the named binding as a VocabularyRegistry.
/// Pure function: no file I/O, no FCS typecheck — implFiles must be pre-typechecked.
/// Useful for testing the walk in isolation or re-evaluating multiple bindings without re-checking.
val evalImplFiles:
    implFiles: FSharpImplementationFileContents list -> bindingName: string -> Result<VocabularyRegistry, string>

/// Evaluate the project's vocabulary CE by FCS-typechecking the source files and
/// walking the typed AST. No code execution, no reflection.
///
/// assemblyRefs: explicit paths to referenced assemblies (must include Frank.Semantic.dll).
///
/// sourceFiles: F# source files in dependency order. The last file drives SDK resolution.
///   All files are typechecked together.
///
/// bindingName: the binding name of the registry (e.g. "CliTestVocab.registry").
///   Qualified or simple names are both supported.
///
/// Returns the reconstructed VocabularyRegistry or a diagnostic Error string.
/// Not consumed by Frank.Cli/Frank.Cli.MSBuild today (Pipeline.run is the sole in-assembly
/// caller); tested directly via InternalsVisibleTo.
val internal evalRegistry:
    assemblyRefs: string list -> sourceFiles: string list -> bindingName: string -> Result<VocabularyRegistry, string>
