namespace Frank.LinkedData

open System
open System.Reflection
open VDS.RDF
open Frank.GeneratedModuleReflection

/// Resolves a LinkedDataConfig from the generated GeneratedLinkedData module
/// compiled into an application assembly by Frank.Cli.MSBuild.
///
/// An F# module compiled to a static class named GeneratedLinkedData with
/// let-bindings graph and jsonLdContext (exposed as static properties).
module GeneratedLinkedDataResolver =

    let private buildConfig (t: Type) : Result<LinkedDataConfig, string> =
        match readStaticProp<IGraph> "graph" t, readStaticProp<string> "jsonLdContext" t with
        | Ok g, Ok ctx -> Ok { Graph = g; JsonLdContext = ctx }
        | Error e, _ -> Error e
        | _, Error e -> Error e

    /// Build a LinkedDataConfig from an arbitrary Type. Used in tests to exercise
    /// the member-resolution path without needing a real assembly scan.
    let resolveFromType (t: Type) : Result<LinkedDataConfig, string> = buildConfig t

    /// Scan loaded assemblies for a GeneratedLinkedData type and return its
    /// LinkedDataConfig. Fails closed — returns Error with a guidance message if:
    ///   • no GeneratedLinkedData type is found
    ///   • more than one GeneratedLinkedData type is found (ambiguous)
    ///   • the graph or jsonLdContext member is missing or wrong-typed
    let resolveGeneratedConfig (assemblies: Assembly[]) : Result<LinkedDataConfig, string> =
        assemblies
        |> findSinglePublicType "GeneratedLinkedData"
        |> Result.bind buildConfig
