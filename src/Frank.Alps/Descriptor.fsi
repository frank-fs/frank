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
