namespace Frank.LinkedData

open System
open System.Reflection

/// Resolves a LinkedDataConfig from the generated GeneratedLinkedData module
/// compiled into an application assembly by Frank.Cli.MSBuild.
///
/// An F# module compiled to a static class named GeneratedLinkedData with
/// let-bindings graph and jsonLdContext (exposed as static properties).
/// Codegen/interop plumbing — not part of Frank.LinkedData's public API surface;
/// visible only to Frank.LinkedData.Tests via InternalsVisibleTo (#392).
module internal GeneratedLinkedDataResolver =

    /// Build a LinkedDataConfig from an arbitrary Type. Used in tests to exercise
    /// the member-resolution path without needing a real assembly scan.
    val resolveFromType: t: Type -> Result<LinkedDataConfig, string>

    /// Scan loaded assemblies for a GeneratedLinkedData type and return its
    /// LinkedDataConfig. Fails closed — returns Error with a guidance message if:
    ///   • no GeneratedLinkedData type is found
    ///   • more than one GeneratedLinkedData type is found (ambiguous)
    ///   • the graph or jsonLdContext member is missing or wrong-typed
    val resolveGeneratedConfig: assemblies: Assembly[] -> Result<LinkedDataConfig, string>
