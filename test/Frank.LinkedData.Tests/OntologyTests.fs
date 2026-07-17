module Frank.LinkedData.Tests.OntologyTests

open System
open Expecto
open Frank.Semantic
open Frank.LinkedData
open VDS.RDF

let private sampleOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://schema.org/Game"
            EquivalentClass = None
            SeeAlso = []
            Properties =
              [ { Iri = Uri "https://schema.org/position"
                  Domain = Uri "https://schema.org/Game" } ] } ]
      ContextBases = [ Uri "https://schema.org/" ] }

let private enrichedOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://example.org/Thing"
            EquivalentClass = Some(Uri "https://schema.org/Thing")
            SeeAlso = [ Uri "https://example.org/ref" ]
            Properties =
              [ { Iri = Uri "https://example.org/name"
                  Domain = Uri "https://example.org/Thing" } ] } ]
      ContextBases = [] }

// #396 AC2: a relative SeeAlso Uri (never emitted by a correct codegen path, but constructible
// by hand, e.g. a future misclassification) must fail loud at graph-construction time — BEFORE
// Ontology.toGraph ever calls the throwing .AbsoluteUri accessor on it — when no baseUri is
// supplied to rebase it against.
let private relativeSeeAlsoOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://example.org/tictactoe#Game"
            EquivalentClass = None
            SeeAlso = [ Uri("/entity/Q210339", UriKind.Relative) ]
            Properties = [] } ]
      ContextBases = [] }

// #396 fold-in, #396 round 5: the same relative-Uri shape exists on ClassDecl.Iri and
// PropertyDecl.Iri — exactly what LinkedDataEmitter now legitimately produces for the app's own
// declared-only prefix (e.g. ttt:square emitted as UriKind.Relative, #396 round 5). With no
// baseUri supplied, both must still fail loud at graph-construction time, not crash deep inside
// .AbsoluteUri; with a baseUri supplied, both must rebase against it (see rebasing tests below).
let private relativeClassIriOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri("/tictactoe#Game", UriKind.Relative)
            EquivalentClass = None
            SeeAlso = []
            Properties = [] } ]
      ContextBases = [] }

let private relativePropertyIriOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://schema.org/MoveAction"
            EquivalentClass = None
            SeeAlso = []
            Properties =
              [ { Iri = Uri("/tictactoe#square", UriKind.Relative)
                  Domain = Uri "https://schema.org/MoveAction" } ] } ]
      ContextBases = [] }

// #396 second fold-in: sweep the 3 remaining unguarded .AbsoluteUri call sites — EquivalentClass,
// PropertyDecl.Domain (both in addClass), and toJsonLdContext's ContextBases (a different function
// entirely, with no single owning class).
let private relativeEquivalentClassOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://schema.org/MoveAction"
            EquivalentClass = Some(Uri("/tictactoe#Action", UriKind.Relative))
            SeeAlso = []
            Properties = [] } ]
      ContextBases = [] }

let private relativeDomainOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://schema.org/MoveAction"
            EquivalentClass = None
            SeeAlso = []
            Properties =
              [ { Iri = Uri "https://schema.org/square"
                  Domain = Uri("/tictactoe#Action", UriKind.Relative) } ] } ]
      ContextBases = [] }

let private relativeContextBasesOntology: OntologyDecl =
    { Classes = []
      ContextBases = [ Uri("/tictactoe#", UriKind.Relative) ] }

/// Run `f`, returning the exception it raises (if any). Shared by the #396 precondition tests.
let private captureException (f: unit -> unit) : exn option =
    try
        f ()
        None
    with ex ->
        Some ex

/// Run `action`, assert it raises ArgumentException whose message contains every fragment in
/// `expectedFragments`. Shared skeleton for the #396 relative-Uri precondition tests.
let private assertRejectsRelative (action: unit -> unit) (expectedFragments: string list) : unit =
    match captureException action with
    | None -> failwith "expected an exception to be raised for a relative Uri"
    | Some ex ->
        Expect.isTrue (ex :? ArgumentException) $"expected ArgumentException, got {ex.GetType().FullName}"

        for fragment in expectedFragments do
            Expect.stringContains ex.Message fragment $"exception message should contain '{fragment}'"

[<Tests>]
let tests =
    testList
        "Ontology interpreter"
        [ test "toGraph emits owl:Class for each class" {
              let g = Ontology.toGraph None sampleOntology

              let rdfType =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

              let owlClass =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2002/07/owl#Class")

              Expect.isNonEmpty (g.GetTriplesWithPredicateObject(rdfType, owlClass) |> Seq.toList) "owl:Class present"
          }
          test "toGraph emits rdfs:domain for each property" {
              let g = Ontology.toGraph None sampleOntology

              let domain =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2000/01/rdf-schema#domain")

              let cls = g.CreateUriNode(UriFactory.Create "https://schema.org/Game")
              Expect.isNonEmpty (g.GetTriplesWithPredicateObject(domain, cls) |> Seq.toList) "domain → class present"
          }
          test "toJsonLdContext lists external bases (trailing slash trimmed)" {
              let ctx = Ontology.toJsonLdContext None sampleOntology
              Expect.stringContains ctx "\"https://schema.org\"" "base IRI present, slash trimmed"
              Expect.stringContains ctx "@context" "is a @context document"
          }
          test "toGraph emits owl:equivalentClass when EquivalentClass is Some" {
              let g = Ontology.toGraph None enrichedOntology

              let equivalentClass =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2002/07/owl#equivalentClass")

              let schemaObj = g.CreateUriNode(UriFactory.Create "https://schema.org/Thing")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(equivalentClass, schemaObj) |> Seq.toList)
                  "owl:equivalentClass triple present"
          }
          test "toGraph emits rdfs:seeAlso for each SeeAlso entry" {
              let g = Ontology.toGraph None enrichedOntology

              let seeAlso =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2000/01/rdf-schema#seeAlso")

              let refObj = g.CreateUriNode(UriFactory.Create "https://example.org/ref")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(seeAlso, refObj) |> Seq.toList)
                  "rdfs:seeAlso triple present"
          }
          test "toGraph emits rdf:type rdf:Property for each property node" {
              let g = Ontology.toGraph None enrichedOntology

              let rdfType =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

              let rdfProperty =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(rdfType, rdfProperty) |> Seq.toList)
                  "rdf:type rdf:Property triple present"
          }
          test "toGraph rejects a relative SeeAlso Uri with no baseUri (#396 AC2)" {
              assertRejectsRelative
                  (fun () -> Ontology.toGraph None relativeSeeAlsoOntology |> ignore)
                  [ "https://example.org/tictactoe#Game"; "seeAlso" ]
          }
          test "toGraph rejects a relative ClassDecl.Iri with no baseUri (#396 fold-in)" {
              assertRejectsRelative
                  (fun () -> Ontology.toGraph None relativeClassIriOntology |> ignore)
                  [ "/tictactoe#Game" ]
          }
          test "toGraph rejects a relative PropertyDecl.Iri with no baseUri (#396 fold-in)" {
              assertRejectsRelative
                  (fun () -> Ontology.toGraph None relativePropertyIriOntology |> ignore)
                  [ "https://schema.org/MoveAction"; "/tictactoe#square" ]
          }
          test "toGraph rejects a relative EquivalentClass Uri with no baseUri (#396 sweep)" {
              assertRejectsRelative
                  (fun () -> Ontology.toGraph None relativeEquivalentClassOntology |> ignore)
                  [ "https://schema.org/MoveAction"; "/tictactoe#Action" ]
          }
          test "toGraph rejects a relative PropertyDecl.Domain Uri with no baseUri (#396 sweep)" {
              assertRejectsRelative
                  (fun () -> Ontology.toGraph None relativeDomainOntology |> ignore)
                  [ "https://schema.org/MoveAction"; "/tictactoe#Action" ]
          }
          test "toJsonLdContext rejects a relative ContextBases Uri with no baseUri (#396 sweep)" {
              assertRejectsRelative
                  (fun () -> Ontology.toJsonLdContext None relativeContextBasesOntology |> ignore)
                  [ "/tictactoe#" ]
          }

          // ── #396 round 5: rebasing against a supplied baseUri ──────────────────────

          test "toGraph rebases a relative ClassDecl.Iri against a supplied baseUri" {
              let g =
                  Ontology.toGraph (Some(Uri "https://realhost.example")) relativeClassIriOntology

              let node = g.GetUriNode(UriFactory.Create "https://realhost.example/tictactoe#Game")
              Expect.isNotNull node "class Iri rebased to the real origin"

              let staleExampleOrg =
                  g.Triples
                  |> Seq.exists (fun t -> (t.Subject.ToString() + t.Object.ToString()).Contains("example.org"))

              Expect.isFalse staleExampleOrg "no example.org anywhere in the rebased graph"
          }

          test "toGraph rebases a relative PropertyDecl.Iri and leaves the external Domain unchanged" {
              let g =
                  Ontology.toGraph (Some(Uri "https://realhost.example")) relativePropertyIriOntology

              let propNode =
                  g.GetUriNode(UriFactory.Create "https://realhost.example/tictactoe#square")

              Expect.isNotNull propNode "property Iri rebased to the real origin"

              let domainPred =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2000/01/rdf-schema#domain")

              let externalDomain =
                  g.CreateUriNode(UriFactory.Create "https://schema.org/MoveAction")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(domainPred, externalDomain) |> Seq.toList)
                  "external Domain Uri (schema.org) stays absolute, unrebased, even with baseUri supplied"
          }

          test "toGraph rebases a relative EquivalentClass Uri against a supplied baseUri" {
              let g =
                  Ontology.toGraph (Some(Uri "https://realhost.example")) relativeEquivalentClassOntology

              let equivalentClass =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2002/07/owl#equivalentClass")

              let rebased =
                  g.CreateUriNode(UriFactory.Create "https://realhost.example/tictactoe#Action")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(equivalentClass, rebased) |> Seq.toList)
                  "EquivalentClass rebased to the real origin"
          }

          test "toGraph rebases a relative PropertyDecl.Domain Uri against a supplied baseUri" {
              let g =
                  Ontology.toGraph (Some(Uri "https://realhost.example")) relativeDomainOntology

              let domainPred =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2000/01/rdf-schema#domain")

              let rebasedDomain =
                  g.CreateUriNode(UriFactory.Create "https://realhost.example/tictactoe#Action")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(domainPred, rebasedDomain) |> Seq.toList)
                  "Domain rebased to the real origin"
          }

          test "toGraph leaves an absolute (external) Uri unchanged even when baseUri is supplied" {
              let g = Ontology.toGraph (Some(Uri "https://realhost.example")) sampleOntology

              let node = g.GetUriNode(UriFactory.Create "https://schema.org/Game")
              Expect.isNotNull node "external schema.org class Iri is untouched by baseUri"

              let rebasedInstead = g.GetUriNode(UriFactory.Create "https://realhost.example/Game")

              Expect.isNull rebasedInstead "external Uri is never rebased against baseUri"
          }

          test "toJsonLdContext rebases a relative ContextBases Uri against a supplied baseUri" {
              let ctx =
                  Ontology.toJsonLdContext (Some(Uri "https://realhost.example")) relativeContextBasesOntology

              Expect.stringContains ctx "\"https://realhost.example/tictactoe#\"" "context base rebased"
              Expect.isFalse (ctx.Contains "example.org") "no example.org in rebased jsonLdContext"
          }

          test "toJsonLdContext leaves an absolute ContextBases Uri unchanged despite baseUri being supplied" {
              let ctx =
                  Ontology.toJsonLdContext (Some(Uri "https://realhost.example")) sampleOntology

              Expect.stringContains ctx "\"https://schema.org\"" "external base IRI untouched by baseUri"
              Expect.isFalse (ctx.Contains "realhost.example") "external base is never rebased"
          } ]
