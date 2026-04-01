namespace Frank.Statecharts

// ==========================================================================
// HierarchyBridge (issue #242)
//
// Converts a parsed StatechartDocument AST into a HierarchySpec, bridging
// the parser pipeline (SCXML, smcat, XState) and the hierarchical runtime.
//
// Rules:
//   - Only StateNodes with non-empty Children are emitted as CompositeStateSpec.
//   - StateNodes with Identifier = None are skipped.
//   - StateKind.Parallel maps to CompositeKind.AND; all other kinds with
//     children map to CompositeKind.XOR.
//   - The InitialChild is the first child with StateKind.Initial; falls back
//     to None when no such child exists.
//   - Nested composites are flattened: all composite nodes at any depth
//     appear as peers in the HierarchySpec.States list.
// ==========================================================================

open Frank.Statecharts.Ast

/// Converts a parsed StatechartDocument into a HierarchySpec for the hierarchical runtime.
[<RequireQualifiedAccess>]
module HierarchyBridge =

    /// Determine the CompositeKind for a StateNode.
    /// Parallel -> AND; all others with children -> XOR.
    let private compositeKind (node: StateNode) : CompositeKind =
        match node.Kind with
        | StateKind.Parallel -> CompositeKind.AND
        | _ -> CompositeKind.XOR

    /// Find the initial child identifier among a node's children.
    /// Returns the identifier of the first child with StateKind.Initial.
    let private findInitialChild (children: StateNode list) : string option =
        children
        |> List.tryPick (fun child ->
            if child.Kind = StateKind.Initial then
                child.Identifier
            else
                None)

    /// Recursively walk a StateNode and collect CompositeStateSpec records
    /// for all composite nodes (those with at least one identifiable child).
    let rec private collectComposites (node: StateNode) : CompositeStateSpec list =
        match node.Identifier with
        | None ->
            // No identifier: skip this node but still recurse into its children
            node.Children |> List.collect collectComposites
        | Some parentId ->
            // Only emit a CompositeStateSpec if the node has at least one child
            let identifiableChildren = node.Children |> List.choose (fun c -> c.Identifier)

            let thisSpec =
                if List.isEmpty node.Children then
                    []
                else
                    [ { Id = parentId
                        Kind = compositeKind node
                        Children = identifiableChildren
                        InitialChild = findInitialChild node.Children
                        CompletionTarget = None } ]

            let childSpecs = node.Children |> List.collect collectComposites
            thisSpec @ childSpecs

    /// Convert a StatechartDocument into a HierarchySpec.
    /// Returns an empty HierarchySpec when the document has no composite states.
    let fromDocument (doc: StatechartDocument) : HierarchySpec =
        let composites =
            doc.Elements
            |> List.collect (function
                | StateDecl node -> collectComposites node
                | _ -> [])

        { States = composites }
