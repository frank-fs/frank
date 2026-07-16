module Frank.Validation.JsonLdLoader

/// Offline JSON-LD context loader. For a context IRI that matches a known vocabulary
/// namespace, returns a synthesized {"@context":{"@vocab": ns}} document so bare terms
/// expand by concatenation to the SAME IRIs Frank's shapes use. Fails closed (throws)
/// for any unknown context IRI — a validator must never let missing context look like conforming data.
val synthesizing: namespaces: string seq -> Frank.Validation.JsonLdDocumentLoader
