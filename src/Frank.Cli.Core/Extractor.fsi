module Frank.Cli.Core.Extractor

open FSharp.Compiler.Symbols
open Frank.Semantic

/// Extract TypeInfo records from F# source code given as a string.
/// Types from referenced assemblies are excluded; only types whose declaration
/// location resolves to the virtual source file are returned.
/// Used internally by Pipeline.run; tested directly via InternalsVisibleTo.
val internal extractTypeInfosFromSource: sourceCode: string -> Result<TypeInfo list, string>

/// Extract TypeInfo records for all F# record and DU types defined in the
/// project's own source files. Cross-project / NuGet types are excluded.
/// Not consumed by Frank.Cli/Frank.Cli.MSBuild today; tested directly via InternalsVisibleTo.
val internal extractTypeInfos: projectFile: string -> Result<TypeInfo list, string>

/// Extract TypeInfo records from a pre-typechecked project signature.
/// signatureEntities: top-level entities from FSharpCheckProjectResults.AssemblySignature.Entities.
/// projectFiles: absolute paths to the project's own source files.
/// Only types declared in projectFiles are returned; cross-project types are excluded.
/// No ParseAndCheckProject call — the caller has already performed the typecheck.
val extractTypeInfosFromEntities: signatureEntities: FSharpEntity seq -> projectFiles: Set<string> -> TypeInfo list

/// Extract TypeInfo records from a set of on-disk F# source files and explicit
/// assembly references.  Reads each file from disk, builds FSharpProjectOptions,
/// and runs ParseAndCheckProject.  Only types whose declaration location resolves
/// to one of the given sourceFiles are returned.
/// Not consumed by Frank.Cli/Frank.Cli.MSBuild today; tested directly via InternalsVisibleTo.
val internal extractTypeInfosFromSources: sourceFiles: string[] -> refs: string[] -> Result<TypeInfo list, string>
