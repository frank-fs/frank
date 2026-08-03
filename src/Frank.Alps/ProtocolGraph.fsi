namespace Frank.Alps

/// One edge in the protocol graph derived from authored descriptors: `Transition` is valid from
/// `FromState`, and moves to `ToState`. Traced to
/// https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/'s
/// `Transition<'State,'Message> = { FromState; Message; ToState }`, generalized to `Descriptor`.
type ProtocolTransition =
    { FromState: Descriptor
      Transition: Descriptor
      ToState: Descriptor }

module ProtocolGraph =
    /// Derives every ProtocolTransition edge from a profile's authored descriptors, walking nested
    /// `Descriptors` recursively. A descriptor declaring both `From` (non-empty) and `Rt` (`Some`)
    /// yields one edge per `From` element; anything else yields none.
    val ofProfile: profile: Descriptor list -> ProtocolTransition list
