namespace Frank.Rdf

[<AutoOpen>]
module Builder =
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
        // Not `inline`: FS1113 -- the implementation captures `subject`, a private constructor
        // field, which isn't accessible enough for source-level inlining across assembly boundaries.
        member Yield: 'a -> Description
        member Zero: unit -> Description
        member inline Run: d: Description -> Description

        [<CustomOperation("typ")>]
        member inline Typ: d: Description * curie: string -> Description

        // `property` is overloaded over 5 types in the brief, but F#'s custom-operation overload
        // resolution commits to a single resolved parameter type for the whole CE once one call to
        // `property` is type-checked, so a block calling `property` with a string then an int then a
        // bool etc. fails to type-check every call after the first (confirmed by direct testing -- see
        // task-3-report.md). Falling back to distinct operation names per the brief's documented escape
        // hatch.
        [<CustomOperation("propertyString")>]
        member inline PropertyString: d: Description * predicate: string * value: string -> Description

        [<CustomOperation("propertyInt")>]
        member inline PropertyInt: d: Description * predicate: string * value: int -> Description

        [<CustomOperation("propertyBool")>]
        member inline PropertyBool: d: Description * predicate: string * value: bool -> Description

        [<CustomOperation("propertyDateTime")>]
        member inline PropertyDateTime: d: Description * predicate: string * value: System.DateTimeOffset -> Description

        [<CustomOperation("propertyLangString")>]
        member inline PropertyLangString: d: Description * predicate: string * value: string * language: string -> Description

        [<CustomOperation("propertyNode")>]
        member inline PropertyNode: d: Description * predicate: string * value: Node -> Description

    /// Enters a `describe { }` block: `describe (Node.Iri "https://example.org/g1") { typ "schema:Game" }`.
    val describe: subject: Node -> DescribeBuilder

    /// Builds a `Doc`. Mirrors Frank core's `ResourceBuilder`/`resource { }` exactly: one accumulator,
    /// no Combine/Delay -- `about` and `triple` each take and return the same `Doc`, the same way
    /// `resource { }`'s `get`/`post` take and return one `ResourceSpec`.
    [<Sealed>]
    type RdfBuilder =
        new: unit -> RdfBuilder
        member inline Yield: 'a -> Doc
        member inline Run: doc: Doc -> Doc

        [<CustomOperation("prefix")>]
        member inline Prefix: doc: Doc * name: string * uri: string -> Doc

        [<CustomOperation("about")>]
        member inline About: doc: Doc * d: Description -> Doc

        [<CustomOperation("triple")>]
        member inline Triple: doc: Doc * subject: Node * predicate: string * value: Value -> Doc

        [<CustomOperation("includeDoc")>]
        member inline IncludeDoc: doc: Doc * other: Doc -> Doc

    /// Enters an `rdf { }` block.
    val rdf: RdfBuilder
