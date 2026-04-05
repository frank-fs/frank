module Frank.Statecharts.TransitionExtractor

open Frank.Statecharts.Ast
open Frank.Resources.Model

// ---------------------------------------------------------------------------
// Bridge from parsed StatechartDocument to TransitionSpec list
// ---------------------------------------------------------------------------

/// Extract RoleConstraint from a transition edge's annotations.
/// Finds AlpsRole annotations with ProjectedRole kind and
/// collects their values into RestrictedTo; no matches yields Unrestricted.
let private resolveConstraint (annotations: Annotation list) : RoleConstraint =
    let roleValues =
        annotations
        |> List.choose (fun ann ->
            match ann with
            | AlpsAnnotation(AlpsRole(ProjectedRole, value, _)) -> Some value
            | _ -> None)

    match roleValues with
    | [] -> Unrestricted
    | values -> RestrictedTo values

/// Extract RoleInfo list from document-level annotations.
/// Roles declared at the document level appear as AlpsRole annotations
/// with ProjectedRole kind. Comma-separated values are split into
/// individual roles (e.g., "PlayerX,PlayerO,Spectator" → 3 RoleInfo).
let extractRoles (doc: StatechartDocument) : RoleInfo list =
    doc.Annotations
    |> List.collect (fun ann ->
        match ann with
        | AlpsAnnotation(AlpsRole(ProjectedRole, value, _)) ->
            value.Split(
                ',',
                System.StringSplitOptions.RemoveEmptyEntries
                ||| System.StringSplitOptions.TrimEntries
            )
            |> Array.toList
            |> List.map (fun name -> { Name = name; Description = None })
        | _ -> [])

/// Extract TransitionSpec list from a StatechartDocument.
/// Iterates doc.Elements, collects TransitionElement edges, and maps each
/// to a domain-neutral TransitionSpec with pre-resolved role constraints.
let extract (doc: StatechartDocument) : TransitionSpec list =
    doc.Elements
    |> List.choose (fun elem ->
        match elem with
        | TransitionElement edge ->
            Some
                { Event = edge.Event |> Option.defaultValue "unknown"
                  Source = edge.Source
                  Target = edge.Target |> Option.defaultValue edge.Source
                  Guard = edge.Guard
                  Constraint = resolveConstraint edge.Annotations
                  Safety = Unsafe
                  SenderRole = edge.SenderRole
                  ReceiverRole = edge.ReceiverRole }
        | _ -> None)
