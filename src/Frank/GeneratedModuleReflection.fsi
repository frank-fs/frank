namespace Frank

open System
open System.Reflection

/// Shared assembly-scan logic used by generated-module resolvers
/// (Frank.Discovery, Frank.LinkedData, Frank.Validation, Frank.Provenance). ONE
/// implementation, four consumers (rule 8). Codegen/interop plumbing — not part
/// of Frank's public API surface; visible only to the generated-module resolvers
/// via InternalsVisibleTo (#392).
module internal GeneratedModuleReflection =

    /// Scan `assemblies` for a single public type with the given `simpleName`.
    /// Returns:
    ///   Ok t       — exactly one match
    ///   Error msg  — none found, or ambiguous (>1 found)
    /// Skips dynamic and System/Microsoft/mscorlib assemblies.
    /// Handles ReflectionTypeLoadException per assembly (bounded: finite assembly list).
    val findSinglePublicType: simpleName: string -> assemblies: Assembly[] -> Result<Type, string>

    /// Read a public static property of type 'T from the given type.
    /// Returns Error with a descriptive message if missing or wrong type.
    val readStaticProp<'T> : propName: string -> t: Type -> Result<'T, string>
