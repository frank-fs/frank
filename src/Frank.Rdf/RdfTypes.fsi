namespace Frank.Rdf

open System

/// A subject, or a reference-valued object: an absolute IRI (or a "prefix:local" CURIE), or a blank node.
[<Struct>]
[<RequireQualifiedAccess>]
type Node =
    | Iri of string
    | Blank of string

/// An RDF literal value.
[<RequireQualifiedAccess>]
type Literal =
    | String of string
    | Int of int
    | Bool of bool
    | DateTime of DateTimeOffset
    /// A language-tagged string (rdf:langString), e.g. "Tic-tac-toe"@en -- (value, BCP47 language tag).
    | LangString of string * string

/// The object of a triple: either a reference to another resource, or a literal.
[<Struct>]
[<RequireQualifiedAccess>]
type Value =
    | Node of node: Node
    | Literal of literal: Literal

/// Statements about one subject, produced by `describe { }` before being attached to a `Doc` via `about`.
type Description =
    { Subject: Node
      Statements: (string * Value) list }

/// Mints RDF nodes.
module Node =
    /// A fresh blank node with a globally-unique label (a GUID) -- so that Doc.merge (see Doc.fsi)
    /// can never collide two unrelated blank nodes minted by two independently-built documents.
    val blank: unit -> Node

/// Well-known RDF vocabulary constants.
[<AutoOpen>]
module RdfVocabulary =
    /// rdf:type, as an absolute IRI. `typ` asserts a statement with this predicate directly, never
    /// resolved through a declared prefix -- it's a universal RDF constant, not app vocabulary.
    val RdfTypeIri: string

/// CURIE/IRI resolution against declared prefixes.
[<AutoOpen>]
module Iri =
    /// Resolves a CURIE ("prefix:local") against declared prefixes, or passes an absolute IRI through
    /// unchanged. A declared prefix always takes priority over "is this already a well-formed URI" --
    /// see the comment on the .fs implementation for why the other order is a real bug, not a style choice.
    /// When the text before the colon isn't a declared prefix, the string only passes through as an
    /// absolute IRI if it looks genuinely absolute -- the part immediately after the parsed scheme's
    /// colon starts with "//" (not merely "://" appearing anywhere later in the string, which would
    /// wrongly admit a typo like "schema:http://weird"), or the string starts with an allow-listed
    /// non-hierarchical scheme ("urn:", "mailto:", "tel:", matched case-insensitively per RFC 3986 §3.1)
    /// -- *and* is well-formed under System.Uri.IsWellFormedUriString. Anything else raises, including
    /// strings that System.Uri.IsWellFormedUriString alone would call well-formed (almost any
    /// "word:word" string qualifies under its loose absolute-URI rules, which is why that check alone
    /// isn't enough to catch a typo'd, undeclared CURIE prefix like "foaf:name"). Raises if there's no
    /// colon at all.
    val internal resolveIri: prefixes: (string * string) list -> s: string -> string

    /// Raises if the same prefix name appears more than once with different URIs.
    val internal validatePrefixes: prefixes: (string * string) list -> unit
