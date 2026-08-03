namespace Frank.Rdf

[<AutoOpen>]
module Rdf =
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

    /// rdf:type, as an absolute IRI. `typ` asserts a statement with this predicate directly, never
    /// resolved through a declared prefix -- it's a universal RDF constant, not app vocabulary.
    val RdfTypeIri: string

    /// Builds a `Description`: statements about one subject, to be attached to an `rdf { }` document
    /// via `about`. Self-contained -- mirrors Frank core's `HandlerBuilder`/`handler { }` exactly: one
    /// accumulator, no Combine/Delay, `Run` returns a plain value.
    ///
    /// `Yield` is generic (`'a -> Description`), not `unit -> Description`, matching
    /// `HandlerBuilder.Yield: 'T -> HandlerDefinition` -- this is required, not stylistic. F#'s custom
    /// operations desugar `describe subject { typ "x" }` into `b.Typ(b.Yield(()), "x")`, i.e. `Yield` is
    /// invoked with an explicit unit-typed seed value. A signature file has no syntax that distinguishes
    /// a member taking a real `unit`-typed argument from a nullary member -- `member Yield: unit ->
    /// Description` always matches only the nullary (`Yield()`) implementation, so it can never match a
    /// `Yield(_: unit)` implementation, and a `Yield()` implementation can't be *called* with the seed
    /// value the custom-operation desugaring passes. Making the parameter generic sidesteps the
    /// ambiguity entirely (`unit` unifies with `'a` at the call site) and is what Frank core's own
    /// builders do.
    ///
    /// `Zero` is also required, despite not appearing in this type's originating brief: an
    /// entirely-`()`-bodied block (`describe subject { () }`, with no custom operation and nothing
    /// yielded) desugars to `b.Zero()`, not `b.Yield(())` -- omitting `Zero` fails with FS0708 ("this
    /// control construct may only be used if the computation expression builder defines a 'Zero'
    /// method"), confirmed by direct testing.
    [<Sealed>]
    type DescribeBuilder =
        new: subject: Node -> DescribeBuilder
        member Yield: 'a -> Description
        member Zero: unit -> Description
        member Run: d: Description -> Description

        [<CustomOperation("typ")>]
        member Typ: d: Description * curie: string -> Description

        // `property` is overloaded over 5 types in the brief, but F#'s custom-operation overload
        // resolution commits to a single resolved parameter type for the whole CE once one call to
        // `property` is type-checked, so a block calling `property` with a string then an int then a
        // bool etc. fails to type-check every call after the first (confirmed by direct testing -- see
        // task-3-report.md). Falling back to distinct operation names per the brief's documented escape
        // hatch.
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

    /// Builds a `Doc`. Mirrors Frank core's `ResourceBuilder`/`resource { }` exactly: one accumulator,
    /// no Combine/Delay -- `about` and `triple` each take and return the same `Doc`, the same way
    /// `resource { }`'s `get`/`post` take and return one `ResourceSpec`.
    [<Sealed>]
    type RdfBuilder =
        new: unit -> RdfBuilder
        member Yield: 'a -> Doc
        member Run: doc: Doc -> Doc

        [<CustomOperation("prefix")>]
        member Prefix: doc: Doc * name: string * uri: string -> Doc

        [<CustomOperation("about")>]
        member About: doc: Doc * d: Description -> Doc

        [<CustomOperation("triple")>]
        member Triple: doc: Doc * subject: Node * predicate: string * value: Value -> Doc

        [<CustomOperation("includeDoc")>]
        member IncludeDoc: doc: Doc * other: Doc -> Doc

    /// Enters an `rdf { }` block.
    val rdf: RdfBuilder

    /// Serializes a Doc's triples and building blocks.
    module Doc =
        /// Builds a dotNetRDF Graph: registers declared prefixes, resolves every Node.Iri/CURIE, mints
        /// one real blank node per distinct Node.Blank label, and asserts one triple per statement.
        /// Raises the same way `resolveIri`/`validatePrefixes` do, for the same reasons.
        val toGraph: doc: Doc -> VDS.RDF.Graph

        /// Writes JSON-LD in expanded form directly into the given TextWriter -- an array with one
        /// node-object per distinct subject, no @context, every predicate and type fully expanded to
        /// its absolute IRI. There is no compact-form option -- see the design doc for why. Never closes
        /// or disposes the writer; the caller owns it (pass one wrapping a response stream to avoid
        /// materializing the whole document as a string first).
        val writeJsonLd: doc: Doc -> writer: System.IO.TextWriter -> unit

        /// Convenience wrapper over writeJsonLd for callers that need the whole document as a string
        /// (tests that reparse it, mainly). Prefer writeJsonLd directly when writing to a response.
        val toJsonLd: doc: Doc -> string

        /// Combines two independently-built documents: concatenates Prefixes and Statements, nothing
        /// more. Safe because Node.blank mints a GUID (never a per-Doc counter, see RdfTypes.fsi) and
        /// because prefix-conflict/duplicate-statement handling already lives in toGraph, not here.
        val merge: a: Doc -> b: Doc -> Doc
