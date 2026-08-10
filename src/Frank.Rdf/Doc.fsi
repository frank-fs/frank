namespace Frank.Rdf

/// A flat set of RDF triples plus the namespace prefixes used to author them.
type Doc =
    { Prefixes: (string * string) list
      Statements: (Node * string * Value) list }

    static member Empty: Doc

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

    /// Writes JSON-LD in expanded form asynchronously into the given IBufferWriter<byte> (e.g.
    /// HttpResponse.BodyWriter / PipeWriter). Best for streaming to response bodies: encodes UTF8
    /// directly to the buffer with no intermediate string allocation or copying. Returns a Task
    /// completed after serialization and flushing to the buffer.
    val writeJsonLdAsync: doc: Doc -> bufferWriter: System.Buffers.IBufferWriter<byte> -> System.Threading.Tasks.Task

    /// Convenience wrapper over writeJsonLd for callers that need the whole document as a string
    /// (tests that reparse it, mainly). Prefer writeJsonLd directly when writing to a response.
    val toJsonLd: doc: Doc -> string

    /// Combines two independently-built documents: concatenates Prefixes and Statements, nothing
    /// more. Safe because Node.blank mints a GUID (never a per-Doc counter, see RdfTypes.fsi) and
    /// because prefix-conflict/duplicate-statement handling already lives in toGraph, not here.
    val merge: a: Doc -> b: Doc -> Doc
