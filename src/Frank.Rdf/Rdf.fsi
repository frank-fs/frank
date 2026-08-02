namespace Frank.Rdf

[<AutoOpen>]
module Rdf =
    /// Resolves a CURIE ("prefix:local") against declared prefixes, or passes an absolute IRI through
    /// unchanged. A declared prefix always takes priority over "is this already a well-formed URI" --
    /// see the comment on the .fs implementation for why the other order is a real bug, not a style choice.
    /// Raises if the text before the colon isn't a declared prefix and the whole string isn't a
    /// well-formed absolute URI either. Raises if there's no colon at all.
    val internal resolveIri: prefixes: (string * string) list -> s: string -> string

    /// Raises if the same prefix name appears more than once with different URIs.
    val internal validatePrefixes: prefixes: (string * string) list -> unit

    /// rdf:type, as an absolute IRI. `typ` asserts a statement with this predicate directly, never
    /// resolved through a declared prefix -- it's a universal RDF constant, not app vocabulary.
    val RdfTypeIri: string

    /// Builds a `Description`: statements about one subject, to be attached to an `rdf { }` document
    /// via `about`. Self-contained -- mirrors Frank core's `HandlerBuilder`/`handler { }` exactly: one
    /// accumulator, no Combine/Delay, `Run` returns a plain value.
    type DescribeBuilder =
        new: subject: Node -> DescribeBuilder
        member Yield: unit -> Description
        member Zero: Description
        member Run: d: Description -> Description

        [<CustomOperation("typ")>]
        member Typ: d: Description * curie: string -> Description

        [<CustomOperation("propertyString")>]
        member PropertyString: d: Description * predicate: string * value: string -> Description

        [<CustomOperation("propertyInt")>]
        member PropertyInt: d: Description * predicate: string * value: int -> Description

        [<CustomOperation("propertyBool")>]
        member PropertyBool: d: Description * predicate: string * value: bool -> Description

        [<CustomOperation("propertyDateTime")>]
        member PropertyDateTime: d: Description * predicate: string * value: System.DateTimeOffset -> Description

        [<CustomOperation("propertyNode")>]
        member PropertyNode: d: Description * predicate: string * value: Node -> Description

    /// Enters a `describe { }` block: `describe (Node.Iri "https://example.org/g1") { typ "schema:Game" }`.
    val describe: subject: Node -> DescribeBuilder
