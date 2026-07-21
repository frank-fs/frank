module TicTacToe.VocabularyEx

open Frank.Semantic

// Minimal ex: vocabulary: declare the ex# prefix and the ttt: domain prefix. No
// "using" directive — the ex: namespace has no published OWL/RDF file to fetch,
// so convention matching is skipped. All IRIs are confirmed manually via the CLI
// accept pipeline (frank semantic accept), not by convention scoring.
//
// The base URI below is a declared-only/owned identity key (EmitterShared.
// declaredOnlyBases, #396/#415) — it is NEVER served on the wire as-is: codegen
// (SemanticModelEmitter/DiscoveryEmitter/LinkedDataEmitter) relativizes every IRI
// under it to a host-relative path, resolved against the sample's own live
// request origin at request time (Frank.UriResolution.resolveAgainst). It is a
// stable identity, not a domain the sample claims to serve directly — hence the
// RFC 2606 ".invalid" TLD: a fixed placeholder domain nobody serves must never
// leak into live output (#415).
let registry =
    vocabulary {
        prefix "ex" "https://tictactoe.invalid/ex#"
        prefix "ttt" "https://tictactoe.invalid/tictactoe#"
    }
