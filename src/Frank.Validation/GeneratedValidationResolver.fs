namespace Frank.Validation

open System
open System.Reflection
open VDS.RDF.Shacl
open Frank.GeneratedModuleReflection

module GeneratedValidationResolver =

    let private buildConfig (t: Type) : Result<ValidationConfig, string> =
        match readStaticProp<ShapesGraph> "shapesGraph" t, readStaticProp<string[]> "knownNamespaces" t with
        | Ok s, Ok ns ->
            Ok
                { Shapes = s
                  ContextLoader = JsonLdLoader.synthesizing ns }
        | Error e, _ -> Error e
        | _, Error e -> Error e

    /// Build a ValidationConfig from an arbitrary Type. Used in tests to exercise
    /// the member-resolution path without needing a real assembly scan.
    let resolveFromType (t: Type) : Result<ValidationConfig, string> = buildConfig t

    /// Scan loaded assemblies for a GeneratedValidation type and return its
    /// ValidationConfig. Fails closed — returns Error with a guidance message if:
    ///   • no GeneratedValidation type is found
    ///   • more than one GeneratedValidation type is found (ambiguous)
    ///   • the shapesGraph member is missing or wrong-typed
    let resolveGeneratedConfig (assemblies: Assembly[]) : Result<ValidationConfig, string> =
        assemblies
        |> findSinglePublicType "GeneratedValidation"
        |> Result.bind buildConfig
