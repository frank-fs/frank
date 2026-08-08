namespace Frank.Alps

open System

[<AutoOpen>]
module DescriptorFunctions =
    /// Constructs a bare `Descriptor` of the given `DescriptorType` -- `Id` set, everything else empty.
    val private makeDescriptor: id: string -> descriptorType: DescriptorType -> Descriptor

    /// A semantic (state/data) descriptor -- the spec's default `type` when omitted.
    val semantic: id: string -> Descriptor

    /// A safe (idempotent, side-effect-free) transition descriptor -- valid HTTP methods: GET, HEAD.
    val safe: id: string -> Descriptor

    /// An unsafe transition descriptor -- valid HTTP method: POST.
    val unsafe: id: string -> Descriptor

    /// An idempotent, non-safe transition descriptor -- valid HTTP methods: PUT, DELETE.
    val idempotent: id: string -> Descriptor

    /// Sets `doc` from plain text -- shorthand for the common case. Use `docWith` for href/format/contentType/tag.
    val doc: text: string -> Descriptor -> Descriptor

    /// Sets `doc` from a full `Doc` record.
    val docWith: doc: Doc -> Descriptor -> Descriptor

    /// Sets `def` -- the descriptor's source-definition IRI. Raises if `iri` isn't a well-formed absolute URI.
    val def: iri: string -> Descriptor -> Descriptor

    /// Appends a `tag` value (draft-07 §2.2.14: whitespace-separated list of non-unique values).
    val tag: value: string -> Descriptor -> Descriptor

    /// Sets `rel` -- an RFC 8288 relation type.
    val rel: relation: string -> Descriptor -> Descriptor

    /// Sets `name` -- rare; only for describing a pre-existing design where the descriptor's id conflicts
    /// with another name (draft-07 §2.2.11).
    val named: name: string -> Descriptor -> Descriptor

    /// Appends an `ext` element with `id` and `value` set (shorthand). Use `extWith` for href/tag.
    val ext: id: string -> value: string -> Descriptor -> Descriptor

    /// Appends a full `Ext` record verbatim.
    val extWith: ext: Ext -> Descriptor -> Descriptor

    /// Appends an RFC 8288 `link` element with `href` and `rel` set (shorthand). Use `linkWith` for title/tag.
    /// Distinct from `href`/`hrefExternal` (descriptor inheritance) -- this is an arbitrary web link, e.g.
    /// `rel="tag-doc"` per draft-07 §2.2.14's guidance for documenting tag vocabularies.
    val link: href: string -> rel: string -> Descriptor -> Descriptor

    /// Appends a full `Link` record verbatim.
    val linkWith: link: Link -> Descriptor -> Descriptor

    /// Two of the canonical Frank.Alps ext ids under the shared https://frank-fs.github.io/alps-ext/
    /// namespace (protocolState/availableInStates, from PR #165/#214, are declared in Serialization.fsi --
    /// Task 8, alongside the projection logic that's their only user).
    [<Literal>]
    val InitialExtId: string = "https://frank-fs.github.io/alps-ext/initial"

    [<Literal>]
    val OrthogonalExtId: string = "https://frank-fs.github.io/alps-ext/orthogonal"

    /// Helper to check if a descriptor has a specific ext id. Internal use only.
    val internal hasExtId: extId: string -> Descriptor -> bool

    /// Sets the nested `descriptor` array (draft-07 §2.2.4). Deliberately untyped by child `DescriptorType`
    /// -- any descriptor may nest under any other. Replaces any previously-set `Descriptors`, unlike the
    /// append-only `tag`/`ext`/`link` -- there is exactly one nested-descriptor array per parent.
    /// Raises if more than one direct child is marked `initial` (via the `initial` function).
    val contains: children: Descriptor list -> Descriptor -> Descriptor

    /// Marks this descriptor as the default child entered when its parent (a composite state) is targeted
    /// without naming a substate. No native ALPS property -- rides `ext` under `InitialExtId`. Any
    /// ALPS-agnostic reader ignores the unrecognized ext element; the document stays fully spec-valid.
    val initial: Descriptor -> Descriptor

    /// Orthogonal (AND) composition, distinct from `contains`'s OR/substate decomposition: `regions
    /// [a; b]` means being in the parent implies being concurrently in some state within *each* of `a`
    /// and `b`. Same `Descriptors` field as `contains`, plus `OrthogonalExtId` on the parent -- no
    /// `Descriptor` shape change. Does not enforce `contains`'s at-most-one-`initial` rule: an AND-region
    /// composition has no single default to disambiguate.
    val regions: children: Descriptor list -> Descriptor -> Descriptor

    /// Sets `rt` -- the target resource type/state for a safe/unsafe/idempotent transition (draft-07
    /// §2.2.13). Descriptor-typed: a dangling reference is a compile error, not a wrong document.
    val rt: target: Descriptor -> Descriptor -> Descriptor

    /// Sets `href` (inheritance) to a descriptor value in this process. Compile-checked, same discipline
    /// as `rt`. Neither this nor `hrefExternal` has a real caller until multi-document profiles exist
    /// (frank-fs/frank#488) -- both exist now so `Descriptor` doesn't need a breaking field change later.
    val href: target: Descriptor -> Descriptor -> Descriptor

    /// Sets `href` (inheritance) to a URI into a document this codebase doesn't own. Nothing to check
    /// against, so a bare string/URI -- the same reasoning that makes a descriptor's own `id` a string.
    val hrefExternal: uri: string -> Descriptor -> Descriptor

    /// Marks a safe/unsafe/idempotent transition as valid only from the given source state(s). Not an
    /// ALPS property -- sets `From`, a Frank.Alps-only field. A transition with no `from` (`From = []`) is
    /// never filtered by state -- graceful degradation, matching how a transition with no auth requirement
    /// is never filtered by authorization. Serialization (Task 8) projects a non-empty `From` into one
    /// `protocolState`/`availableInStates` ext pair per declared state -- `From` itself is not serialized
    /// as ext directly.
    val from: sources: Descriptor list -> Descriptor -> Descriptor

    /// Sets an explicit guard tree, independent of `from`. `ProtocolGraph.ofProfile` prefers `Guard` over
    /// deriving one from `From` when both are present.
    val guardedBy: guard: StateGuard -> Descriptor -> Descriptor

    /// Sets explicit fan-out targets, independent of `rt`. `ProtocolGraph.ofProfile` prefers `Targets` over
    /// deriving one from `Rt` when both are present.
    val entersRegions: targets: TransitionTarget list -> Descriptor -> Descriptor

/// Whether a descriptor's nested `Descriptors` are OR-alternatives (substates -- exactly one is
/// current) or AND-regions (orthogonal -- all are concurrently current), derived by reading the
/// `OrthogonalExtId` marker that `regions` sets. Purely a read of already-authored data -- no runtime
/// execution.
[<RequireQualifiedAccess>]
type StateComposition =
    | Leaf
    | Alternatives of Descriptor list
    | Regions of Descriptor list

[<RequireQualifiedAccess>]
module StateComposition =
    val ofDescriptor: Descriptor -> StateComposition

    /// The child marked `initial`, if any. Meaningful only when `ofDescriptor` returns `Alternatives`
    /// -- an AND-region composition has no single default child.
    val initialChild: Descriptor -> Descriptor option
