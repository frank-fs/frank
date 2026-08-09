namespace Frank.Rdf

open System

/// A subject, or a reference-valued object: an absolute IRI (or a "prefix:local" CURIE), or a blank node.
[<Struct>]
[<RequireQualifiedAccess>]
type Node =
    | Iri of string
    | Blank of string

/// An RDF literal value.
[<Struct>]
[<RequireQualifiedAccess>]
type Literal =
    | String of stringValue: string
    | Int of intValue: int
    | Bool of boolValue: bool
    | DateTime of dateTimeValue: DateTimeOffset
    /// A language-tagged string (rdf:langString), e.g. "Tic-tac-toe"@en -- (value, BCP47 language tag).
    | LangString of text: string * lang: string

/// The object of a triple: either a reference to another resource, or a literal.
[<RequireQualifiedAccess>]
type Value =
    | Node of Node
    | Literal of Literal

/// A flat set of RDF triples plus the namespace prefixes used to author them.
type Doc =
    { Prefixes: (string * string) list
      Statements: (Node * string * Value) list }

    static member Empty: Doc

/// Statements about one subject, produced by `describe { }` before being attached to a `Doc` via `about`.
type Description =
    { Subject: Node
      Statements: (string * Value) list }

/// Mints RDF nodes.
module Node =
    /// A fresh blank node with a globally-unique label (a GUID) -- so that Doc.merge (see Rdf.fsi)
    /// can never collide two unrelated blank nodes minted by two independently-built documents.
    val blank: unit -> Node
