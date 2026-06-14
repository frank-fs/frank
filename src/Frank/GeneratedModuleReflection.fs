namespace Frank

open System
open System.Reflection

/// Shared assembly-scan logic used by generated-module resolvers
/// (Frank.Discovery, Frank.LinkedData). ONE implementation, two consumers (rule 8).
module GeneratedModuleReflection =

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

    /// Scan `assemblies` for a single public type with the given `simpleName`.
    /// Returns:
    ///   Ok t       — exactly one match
    ///   Error msg  — none found, or ambiguous (>1 found)
    /// Skips dynamic and System/Microsoft/mscorlib assemblies.
    /// Handles ReflectionTypeLoadException per assembly (bounded: finite assembly list).
    let findSinglePublicType (simpleName: string) (assemblies: Assembly[]) : Result<Type, string> =
        let candidates =
            assemblies
            |> Array.filter (isSkippable >> not)
            |> Array.collect tryGetTypes
            |> Array.filter (fun t -> t.IsPublic && t.Name = simpleName)
            |> Array.toList

        match candidates with
        | [] ->
            Error
                $"useDiscovery/useLinkedData: no {simpleName} type found in loaded assemblies. \
                 Ensure {simpleName}.fs is generated and compiled into your application \
                 (Frank.Cli.MSBuild generates this file)."
        | [ t ] -> Ok t
        | _ ->
            let names = candidates |> List.map _.AssemblyQualifiedName |> String.concat "; "
            Error $"ambiguous — {candidates.Length} {simpleName} types found: {names}"

    /// Read a public static property of type 'T from the given type.
    /// Returns Error with a descriptive message if missing or wrong type.
    let readStaticProp<'T> (propName: string) (t: Type) : Result<'T, string> =
        let prop = t.GetProperty(propName, BindingFlags.Public ||| BindingFlags.Static)

        if isNull prop then
            Error $"{t.Name} in {t.AssemblyQualifiedName} has no public static property '{propName}'"
        else

            match prop.GetValue null with
            | :? 'T as v -> Ok v
            | _ -> Error $"{t.Name}.{propName} in {t.AssemblyQualifiedName} is not a {typeof<'T>.Name}"
