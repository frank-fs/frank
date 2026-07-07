module Frank.Semantic.VocabCheck

open System
open Frank.Semantic.LockFile

let private routeCoversNsPath (routePath: string) (nsPath: string) : bool =
    routePath = nsPath || nsPath.StartsWith(routePath + "/")

let private extractAbsolutePath (uri: string) : string option =
    match Uri.TryCreate(uri, UriKind.Absolute) with
    | true, u -> Some u.AbsolutePath
    | _ -> None

let private isDereferenceable (prefix: string) (lock: LockFile) : bool =
    Map.containsKey prefix lock.Vocabularies

let private nsUriFor (prefix: string) (lock: LockFile) : string option =
    match Map.tryFind prefix lock.DeclaredPrefixes with
    | Some uri -> Some uri
    | None -> Map.tryFind prefix lock.Vocabularies |> Option.map (fun entry -> entry.Uri)

let private isRouted (nsUri: string) (routes: string list) : bool =
    match extractAbsolutePath nsUri with
    | None -> false
    | Some nsPath -> routes |> List.exists (fun r -> routeCoversNsPath r nsPath)

/// Check each prefix in referencedNs against the lock and routes.
/// Returns the prefixes that are neither dereferenceable (in lock.Vocabularies)
/// nor routed (any route covers the namespace deref path).
/// No network I/O. Deterministic, offline-safe, CI-safe.
let checkUndereferenceableVocab (lock: LockFile) (routes: string list) (referencedNs: string list) : string list =
    referencedNs
    |> List.filter (fun prefix ->
        not (isDereferenceable prefix lock)
        && (match nsUriFor prefix lock with
            | None -> false
            | Some nsUri -> not (isRouted nsUri routes)))
