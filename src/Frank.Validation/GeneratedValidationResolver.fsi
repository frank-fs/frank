namespace Frank.Validation

open System
open System.Reflection

/// Resolves a ValidationConfig from the generated GeneratedValidation module compiled
/// into an application assembly by Frank.Cli.MSBuild. Codegen/interop plumbing — not
/// part of Frank.Validation's public API surface; visible only to
/// Frank.Validation.Tests via InternalsVisibleTo (#392).
module internal GeneratedValidationResolver =

    /// Build a ValidationConfig from an arbitrary Type. Used in tests to exercise
    /// the member-resolution path without needing a real assembly scan.
    val resolveFromType: t: Type -> Result<ValidationConfig, string>

    /// Scan loaded assemblies for a GeneratedValidation type and return its
    /// ValidationConfig. Fails closed — returns Error with a guidance message if:
    ///   • no GeneratedValidation type is found
    ///   • more than one GeneratedValidation type is found (ambiguous)
    ///   • the shapesGraph member is missing or wrong-typed
    val resolveGeneratedConfig: assemblies: Assembly[] -> Result<ValidationConfig, string>
