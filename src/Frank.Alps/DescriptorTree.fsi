namespace Frank.Alps

/// Whole-tree operations over an authored profile.
///
/// `contains` nesting is general (draft-07 §2.2.4 -- any descriptor type may nest under any other), so
/// a transition can sit at any depth of the tree a profile hands to `useAlps`. Every decision this
/// package makes about *which* descriptors a given principal may see therefore has to be made over the
/// whole tree, not over the top-level list -- otherwise a guarded transition nested under a `Semantic`
/// parent is served to everyone, because the parent is kept unconditionally and nothing ever looks
/// inside it. Both HTTP exposures (`AlpsDocument`'s app-wide document and `Alps.excerpt`) walk the tree
/// through this one module rather than each re-deriving its own recursion.
[<RequireQualifiedAccess>]
module DescriptorTree =
    /// Every descriptor reachable from `d`: `d` itself first, then each nested child's own subtree, in
    /// authoring order. Same shape `ProtocolGraph.ofProfile` uses (and now shares) to reach nested
    /// transitions. Termination is structural: `Descriptor` is an immutable record and `contains` can
    /// only take children that already exist, so `Descriptors` nesting is acyclic by construction.
    val flatten: d: Descriptor -> Descriptor list

    /// `flatten` over every root of a profile.
    val flattenAll: profile: Descriptor list -> Descriptor list

    /// Recursively prunes a profile to the descriptors one principal may see, at every depth.
    ///
    /// A `Semantic` descriptor is always kept *for itself* -- vocabulary, not capability, the same rule
    /// the top-level filter has always applied -- but its own children are still pruned recursively, so
    /// a guarded transition nested under a semantic state disappears for a principal who may not invoke
    /// it. A non-`Semantic` (transition) descriptor is kept only if its id is in `allowedIds`, and its
    /// own children are then pruned the same way.
    ///
    /// `allowedIds` is the set of descriptor ids whose bound endpoint the current principal is allowed
    /// to reach (`AuthorizationFilter.filter`). A transition that is in the profile but bound to no
    /// endpoint at all is therefore absent from `allowedIds` and drops out -- fail closed, since there
    /// is no endpoint whose authorization metadata could have been evaluated for it. That case is an
    /// authoring mistake far more often than not, so `AlpsDocument.unboundTransitions` reports it as a
    /// startup warning rather than letting it vanish silently.
    val prune: allowedIds: Set<string> -> profile: Descriptor list -> Descriptor list
