namespace Frank.Discovery

open System.Reflection
open Frank.GeneratedModuleReflection

/// Resolves a DiscoveryConfig from the generated GeneratedDiscovery module
/// compiled into an application assembly by Frank.Cli.MSBuild.
///
/// An F# module compiled to a static class named GeneratedDiscovery with a
/// let-binding discoveryConfig (exposed as a static property).
module GeneratedDiscoveryResolver =

    /// Scan loaded assemblies for a GeneratedDiscovery type and return its
    /// DiscoveryConfig. Fails closed — returns Error with a guidance message if:
    ///   • no GeneratedDiscovery type is found
    ///   • more than one GeneratedDiscovery type is found (ambiguous)
    ///   • the discoveryConfig member is missing or wrong-typed
    let resolveGeneratedConfig (assemblies: Assembly[]) : Result<DiscoveryConfig, string> =
        assemblies
        |> findSinglePublicType "GeneratedDiscovery"
        |> Result.bind (readStaticProp<DiscoveryConfig> "discoveryConfig")
