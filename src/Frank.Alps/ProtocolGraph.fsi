namespace Frank.Alps

/// A derived protocol edge. `FromGuard = None` means the transition is unconditional -- it fires
/// regardless of prior state. `ToTargets` non-empty is the only requirement for an edge to exist.
type ProtocolTransition =
    { FromGuard: StateGuard option
      Transition: Descriptor
      ToTargets: TransitionTarget list }

module ProtocolGraph =
    /// Derives the read-only edge set from an authored profile. `FromGuard` comes from `Guard` if set,
    /// else from `From` (empty -> None, one -> State, many -> Any -- collapses today's per-alternative
    /// expansion into one edge). `ToTargets` comes from `Targets` if non-empty, else from `Rt` (Some ->
    /// one EnterState, None -> empty). An edge is emitted iff the resulting `ToTargets` is non-empty.
    val ofProfile: Descriptor list -> ProtocolTransition list
