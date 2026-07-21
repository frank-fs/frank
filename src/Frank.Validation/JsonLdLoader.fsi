module Frank.Validation.JsonLdLoader

/// Offline JSON-LD context loader. For a context IRI that matches a known vocabulary
/// namespace (exactly, by trailing-slash variant, or by authority — e.g. a real
/// context-document URL under the same host as a registered namespace, #414), returns a
/// synthesized {"@context":{"@vocab": ns}} document so bare terms expand by concatenation to
/// the SAME IRIs Frank's shapes use. rdf/rdfs/owl are always recognized, without needing to
/// be passed in `namespaces` (#414). Fails closed (throws) for any unknown/other-authority
/// context IRI — a validator must never let missing context look like conforming data.
val synthesizing: namespaces: string seq -> Frank.Validation.JsonLdDocumentLoader
