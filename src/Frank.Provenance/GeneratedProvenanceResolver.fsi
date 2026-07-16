namespace Frank.Provenance

open System.Reflection

/// Resolves a ProvenanceConfig from the generated GeneratedProvenance module compiled
/// into an application assembly by Frank.Cli.MSBuild. Codegen/interop plumbing — not
/// part of Frank.Provenance's public API surface; visible only to
/// Frank.Provenance.Tests via InternalsVisibleTo (#392).
module internal GeneratedProvenanceResolver =

    val resolveFromType: t: System.Type -> Result<ProvenanceConfig, string>

    val resolveGeneratedConfig: assemblies: Assembly[] -> Result<ProvenanceConfig, string>
