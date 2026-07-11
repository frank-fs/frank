module Undereferenceable.Resource

// Fixture for CI fsharp-analyzers FRANK002 enforcement.
// The .frank/semantic-mappings.lock.json declares "ext" with an undereferenceable IRI.
// This file references ext:Thing so the prefix is in-scope; the analyzer emits FRANK002.
let extThing = "ext:Thing"
