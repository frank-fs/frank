namespace Frank.LinkedData

open System
open System.Reflection
open VDS.RDF

/// Resolves a LinkedDataConfig from the generated GeneratedLinkedData module
/// compiled into an application assembly by Frank.Cli.MSBuild.
///
/// An F# module compiled to a static class named GeneratedLinkedData with
/// let-bindings graph and jsonLdContext (exposed as static properties).
module GeneratedLinkedDataResolver =

    let private isSkippable (asm: Assembly) =
        asm.IsDynamic
        || asm.FullName.StartsWith("System.", StringComparison.Ordinal)
        || asm.FullName.StartsWith("Microsoft.", StringComparison.Ordinal)
        || asm.FullName.StartsWith("mscorlib,", StringComparison.Ordinal)

    let private tryGetTypes (asm: Assembly) : Type[] =
        try
            asm.GetTypes()
        with :? ReflectionTypeLoadException as ex ->
            ex.Types |> Array.filter (isNull >> not)

    let private matchesName (t: Type) =
        t.IsPublic && t.Name = "GeneratedLinkedData"

    let private findCandidates (assemblies: Assembly[]) : Type list =
        assemblies
        |> Array.filter (isSkippable >> not)
        |> Array.collect tryGetTypes
        |> Array.filter matchesName
        |> Array.toList

    let private readGraph (t: Type) : Result<IGraph, string> =
        let prop = t.GetProperty("graph", BindingFlags.Public ||| BindingFlags.Static)

        if isNull prop then
            Error $"GeneratedLinkedData in {t.AssemblyQualifiedName} has no public static property 'graph'"
        else

            let value = prop.GetValue null

            match value with
            | :? IGraph as g -> Ok g
            | _ -> Error $"GeneratedLinkedData.graph in {t.AssemblyQualifiedName} is not an IGraph"

    let private readJsonLdContext (t: Type) : Result<string, string> =
        let prop =
            t.GetProperty("jsonLdContext", BindingFlags.Public ||| BindingFlags.Static)

        if isNull prop then
            Error $"GeneratedLinkedData in {t.AssemblyQualifiedName} has no public static property 'jsonLdContext'"
        else

            let value = prop.GetValue null

            match value with
            | :? string as s -> Ok s
            | _ -> Error $"GeneratedLinkedData.jsonLdContext in {t.AssemblyQualifiedName} is not a string"

    let private buildConfig (t: Type) : Result<LinkedDataConfig, string> =
        match readGraph t, readJsonLdContext t with
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
        let candidates = findCandidates assemblies

        match candidates with
        | [] ->
            Error
                "useLinkedData: no GeneratedLinkedData type found in loaded assemblies. \
                 Reference Frank.LinkedData and ensure GeneratedLinkedData.fs is generated \
                 and compiled into your application (Frank.Cli.MSBuild generates this file)."
        | [ t ] -> buildConfig t
        | _ ->
            let names =
                candidates |> List.map (fun t -> t.AssemblyQualifiedName) |> String.concat "; "

            Error $"useLinkedData: ambiguous — {candidates.Length} GeneratedLinkedData types found: {names}"
