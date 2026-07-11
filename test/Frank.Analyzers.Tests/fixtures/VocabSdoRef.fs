module TestFixtures.VocabSdoRef

// References the sdo: prefix (same IRI as schema:, stored under a different key).
// Used for AT7 analyzer-level test: sdo resolves via IRI-identity to a Confirmed
// schema entry, so no FRANK002 is emitted even though the prefix is referenced.
let sdoGame = "sdo:Game"
let sdoPerson = "sdo:Person"
