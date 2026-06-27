namespace Frank.Provenance

open System
open System.Reflection
open Frank.Semantic
open Frank.GeneratedModuleReflection

module GeneratedProvenanceResolver =

    let private parseProvClass (name: string) : Result<ProvOClass, string> =
        match name with
        | "Entity" -> Ok ProvOClass.Entity
        | "Activity" -> Ok ProvOClass.Activity
        | "Agent" -> Ok ProvOClass.Agent
        | other -> Error $"unknown ProvOClass '{other}'"

    let private toEntry (typeName: string, (clsName: string, iri: string)) =
        parseProvClass clsName
        |> Result.map (fun cls ->
            let iriOpt = if String.IsNullOrEmpty iri then None else Some(Uri iri)
            typeName, (cls, iriOpt))

    let private buildConfig
        (entries: (string * (string * string)) list)
        (ns: string[])
        : Result<ProvenanceConfig, string> =
        let folded =
            (Ok [], entries)
            ||> List.fold (fun acc e ->
                match acc, toEntry e with
                | Error x, _ -> Error x
                | _, Error x -> Error x
                | Ok xs, Ok x -> Ok(x :: xs))

        folded
        |> Result.map (fun pairs ->
            { ProvClasses = Map.ofList pairs
              KnownNamespaces = ns
              StoreConfig = ProvenanceStoreConfig.defaults })

    let private readConfig (t: Type) : Result<(string * (string * string)) list * string[], string> =
        match
            readStaticProp<(string * (string * string)) list> "provClasses" t,
            readStaticProp<string[]> "knownNamespaces" t
        with
        | Ok entries, Ok ns -> Ok(entries, ns)
        | Error e, _ -> Error e
        | _, Error e -> Error e

    let resolveFromType (t: Type) : Result<ProvenanceConfig, string> =
        readConfig t |> Result.bind (fun (entries, ns) -> buildConfig entries ns)

    let resolveGeneratedConfig (assemblies: Assembly[]) : Result<ProvenanceConfig, string> =
        assemblies
        |> findSinglePublicType "GeneratedProvenance"
        |> Result.bind (fun t -> readConfig t |> Result.bind (fun (entries, ns) -> buildConfig entries ns))
