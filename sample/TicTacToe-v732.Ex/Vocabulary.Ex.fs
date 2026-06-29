module TicTacToe.VocabularyEx

open Frank.Semantic

// Minimal ex: vocabulary: declare the example.org/ex# prefix and the ttt: domain
// prefix. No "using" directive — the ex: namespace has no published OWL/RDF file
// to fetch, so convention matching is skipped. All IRIs are confirmed manually via
// the CLI accept pipeline (frank semantic accept), not by convention scoring.
let registry =
    vocabulary {
        prefix "ex" "https://example.org/ex#"
        prefix "ttt" "https://example.org/tictactoe#"
    }
