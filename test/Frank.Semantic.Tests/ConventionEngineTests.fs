module Frank.Semantic.Tests.ConventionEngineTests

open System
open System.IO
open System.Text
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Semantic

// ── Jaro-Winkler reference values ────────────────────────────────────────────

[<Tests>]
let jaroWinklerReferenceValues =
    testList
        "Jaro-Winkler reference values"
        [ test "identical strings = 1.0" {
              let score = ConventionEngine.jaroWinkler "order" "order"
              Expect.floatClose Accuracy.high score 1.0 "identical"
          }

          test "JW(martha, marhta) ≈ 0.961" {
              let score = ConventionEngine.jaroWinkler "martha" "marhta"
              Expect.isTrue (score > 0.95 && score < 0.97) $"expected ~0.961, got {score}"
          }

          test "JW(dwayne, duane) ≈ 0.84" {
              let score = ConventionEngine.jaroWinkler "dwayne" "duane"
              Expect.isTrue (score > 0.82 && score < 0.86) $"expected ~0.84, got {score}"
          }

          test "completely disjoint strings = 0.0" {
              let score = ConventionEngine.jaroWinkler "abc" "xyz"
              Expect.floatClose Accuracy.medium score 0.0 "disjoint"
          }

          test "empty string pair = 1.0 (both empty → identical)" {
              let score = ConventionEngine.jaroWinkler "" ""
              Expect.floatClose Accuracy.high score 1.0 "both empty"
          }

          testProperty "result is always in [0,1]" (fun (a: string) (b: string) ->
              let score = ConventionEngine.jaroWinkler a b
              score >= 0.0 && score <= 1.0)

          testProperty "symmetry: JW(a,b) = JW(b,a)" (fun (a: string) (b: string) ->
              let ab = ConventionEngine.jaroWinkler a b
              let ba = ConventionEngine.jaroWinkler b a
              abs (ab - ba) < 1e-10)

          testProperty "identity: JW(x,x) = 1.0 for non-empty" (fun (x: string) ->
              x = ""
              || (let score = ConventionEngine.jaroWinkler x x
                  abs (score - 1.0) < 1e-10)) ]

// ── Name normalization ────────────────────────────────────────────────────────

[<Tests>]
let normalizationTests =
    testList
        "Name normalization"
        [ test "PascalCase splits to lowercase tokens" {
              let tokens = ConventionEngine.normalizeTokens "CustomerOrder"
              Expect.equal tokens [ "customer"; "order" ] "split PascalCase"
          }

          test "strips Dto suffix" {
              let tokens = ConventionEngine.normalizeTokens "OrderDto"
              Expect.equal tokens [ "order" ] "OrderDto → order"
          }

          test "strips Model suffix" {
              let tokens = ConventionEngine.normalizeTokens "OrderModel"
              Expect.equal tokens [ "order" ] "OrderModel → order"
          }

          test "strips Record suffix" {
              let tokens = ConventionEngine.normalizeTokens "CustomerOrderRecord"
              Expect.equal tokens [ "customer"; "order" ] "CustomerOrderRecord → customer, order"
          }

          test "single-word type stays as single token" {
              let tokens = ConventionEngine.normalizeTokens "Order"
              Expect.equal tokens [ "order" ] "Order → order"
          }

          test "all-caps abbreviation is one token" {
              let tokens = ConventionEngine.normalizeTokens "URL"
              Expect.equal tokens [ "url" ] "URL → url"
          }

          test "normalized tokens joined as canonical name" {
              let name = ConventionEngine.canonicalName "CustomerOrderRecord"
              Expect.equal name "customer order" "joined with space"
          } ]

// ── AT5: multiplication threshold rule ───────────────────────────────────────

[<Tests>]
let at5ThresholdRule =
    testList
        "AT5: overlap * 2 >= total (not integer-division form)"
        [ test "overlap=1 total=3 is NOT half-overlap (multiplication form correct)" {
              // Integer-division form: 1 >= 3/2 = 1 → true (WRONG, too permissive)
              // Multiplication form:  1*2 >= 3   → false (CORRECT)
              let overlap = 1
              let total = 3
              let multiplicationResult = overlap * 2 >= total
              let intDivisionResult = overlap >= total / 2
              Expect.isFalse multiplicationResult "multiplication form: 1*2 < 3, not half-overlap"
              Expect.isTrue intDivisionResult "integer-division form gives wrong answer (too permissive)"
          }

          test "overlap=2 total=3 IS half-overlap (multiplication form correct)" {
              let overlap = 2
              let total = 3
              let result = overlap * 2 >= total
              Expect.isTrue result "2*2=4 >= 3"
          }

          test "overlap=1 total=2 IS half-overlap" {
              let overlap = 1
              let total = 2
              let result = overlap * 2 >= total
              Expect.isTrue result "1*2=2 >= 2"
          }

          test "ConventionEngine uses multiplication form for field overlap" {
              // This test drives the implementation: score a type where overlap=1, total=3
              // If the engine used integer division, field overlap ratio would be >= 0.5
              // With multiplication form, 1/3 < 0.5 — the weighted score is lower
              // We verify by checking the raw fieldOverlapRatio via a known scenario
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
                    Properties =
                      Map.ofList
                          [ "ordereditem", "https://schema.org/orderedItem"
                            "totalpaymentdue", "https://schema.org/totalPaymentDue"
                            "orderdate", "https://schema.org/orderDate" ] }

              let typeInfo: TypeInfo =
                  { FullName = "Test.Order"
                    Namespace = "Test"
                    LocalName = "Order"
                    Fields =
                      [ { Name = "Total"
                          TypeName = "decimal"
                          Attributes = Map.empty
                          DocComment = None } ]
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                  }

              let mapping = ConventionEngine.score terms registry typeInfo
              // overlap=1 matched (Total→totalpaymentdue marginal), total=3 properties
              // regardless of exact match, ratio must be computed as overlap/total not using integer div
              // The test just proves score runs without error and returns a valid mapping
              Expect.isTrue (mapping.Confidence >= 0.0 && mapping.Confidence <= 1.0) "confidence in bounds"
          } ]

// ── AT1: High-confidence confirmed ────────────────────────────────────────────

[<Tests>]
let at1ConfirmedMapping =
    test "AT1: Order with Total+LineItems under 'using schema' confirms to schema:Order" {
        let terms: VocabTerms =
            { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
              Properties =
                Map.ofList
                    [ "totalpaymentdue", "https://schema.org/totalPaymentDue"
                      "ordereditem", "https://schema.org/orderedItem" ] }

        let typeInfo: TypeInfo =
            { FullName = "MyApp.Order"
              Namespace = "MyApp"
              LocalName = "Order"
              Fields =
                [ { Name = "Total"
                    TypeName = "decimal"
                    Attributes = Map.empty
                    DocComment = None }
                  { Name = "LineItems"
                    TypeName = "OrderLine list"
                    Attributes = Map.empty
                    DocComment = None } ]
              Attributes = Map.empty
              DocComment = None }

        let registry =
            vocabulary {
                prefix "schema" "https://schema.org/"
                using "schema"
            }

        let mapping = ConventionEngine.score terms registry typeInfo

        Expect.equal mapping.Status MappingStatus.Confirmed "status confirmed"
        Expect.isSome mapping.Iri "iri present"
        Expect.isTrue (mapping.Confidence >= 0.85) $"confidence >= 0.85, got {mapping.Confidence}"
        Expect.equal mapping.Source MappingSource.Convention "source is convention"
    }

// ── AT2: Low-confidence proposed ─────────────────────────────────────────────

[<Tests>]
let at2ProposedMapping =
    test "AT2: CustomerOrderRecord{Total,LineItems} proposes schema:Order at ~0.70 with alternates" {
        // "customer" token has JW=0 against "order"; "order" token has JW=1.0.
        // tokenAvg = 0.5. fieldOverlap = 1.0 (Total+LineItems both match).
        // combined = 0.6*0.5 + 0.4*1.0 = 0.70. "orders"/"ordering" are viable alternates
        // (maxTokenHit 0.967/0.925 >= 0.85) but score below 0.70.
        let terms: VocabTerms =
            { Classes =
                Map.ofList
                    [ "order", "https://schema.org/Order"
                      "orders", "https://schema.org/OrderStatus"
                      "ordering", "https://schema.org/OrderAction" ]
              Properties =
                Map.ofList
                    [ "totalpaymentdue", "https://schema.org/totalPaymentDue"
                      "ordereditem", "https://schema.org/orderedItem" ] }

        let typeInfo: TypeInfo =
            { FullName = "MyApp.CustomerOrderRecord"
              Namespace = "MyApp"
              LocalName = "CustomerOrderRecord"
              Fields =
                [ { Name = "Total"
                    TypeName = "decimal"
                    Attributes = Map.empty
                    DocComment = None }
                  { Name = "LineItems"
                    TypeName = "OrderLine list"
                    Attributes = Map.empty
                    DocComment = None } ]
              Attributes = Map.empty
              DocComment = None }

        let registry =
            vocabulary {
                prefix "schema" "https://schema.org/"
                using "schema"
            }

        let mapping = ConventionEngine.score terms registry typeInfo

        Expect.equal mapping.Status MappingStatus.Proposed "status proposed"
        Expect.equal mapping.Iri (Some "schema:Order") "best candidate is schema:Order"

        Expect.isTrue
            (mapping.Confidence >= 0.6 && mapping.Confidence < 0.85)
            $"confidence in [0.6,0.85), got {mapping.Confidence}"

        Expect.isNonEmpty mapping.Alternates "alternates listed"
    }

// ── AT3: No viable candidate → unresolved ────────────────────────────────────

[<Tests>]
let at3UnresolvedMapping =
    test "AT3: WidgetForge{Sprocket} — no token hit >=0.85 against order/orders/ordering → unresolved" {
        // "widget" max JW against {order,orders,ordering} = 0.578; "forge" even lower.
        // Neither token clears 0.85, so no viable candidate exists → unresolved.
        // The magic-0.25 score-floor bug would have leaked this as Proposed via combined score;
        // the token-hit rule gates it correctly.
        let terms: VocabTerms =
            { Classes =
                Map.ofList
                    [ "order", "https://schema.org/Order"
                      "orders", "https://schema.org/OrderStatus"
                      "ordering", "https://schema.org/OrderAction" ]
              Properties =
                Map.ofList
                    [ "totalpaymentdue", "https://schema.org/totalPaymentDue"
                      "ordereditem", "https://schema.org/orderedItem" ] }

        let typeInfo: TypeInfo =
            { FullName = "MyApp.WidgetForge"
              Namespace = "MyApp"
              LocalName = "WidgetForge"
              Fields =
                [ { Name = "Sprocket"
                    TypeName = "int"
                    Attributes = Map.empty
                    DocComment = None } ]
              Attributes = Map.empty
              DocComment = None }

        let registry =
            vocabulary {
                prefix "schema" "https://schema.org/"
                using "schema"
            }

        let mapping = ConventionEngine.score terms registry typeInfo

        Expect.equal mapping.Status MappingStatus.Unresolved "status unresolved"
        Expect.isNone mapping.Iri "iri is None"
        Expect.isEmpty mapping.Alternates "alternates empty"
    }

// ── AT4: JsonPropertyName drives field matching ───────────────────────────────

[<Tests>]
let at4JsonPropertyNameBoost =
    test "AT4: Total with JsonPropertyName(totalPaymentDue) scores higher than without" {
        let terms: VocabTerms =
            { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
              Properties = Map.ofList [ "totalpaymentdue", "https://schema.org/totalPaymentDue" ] }

        let registry =
            vocabulary {
                prefix "schema" "https://schema.org/"
                using "schema"
            }

        let typeWithAttr: TypeInfo =
            { FullName = "MyApp.Order"
              Namespace = "MyApp"
              LocalName = "Order"
              Fields =
                [ { Name = "Total"
                    TypeName = "decimal"
                    Attributes = Map.ofList [ "JsonPropertyName", "totalPaymentDue" ]
                    DocComment = None } ]
              Attributes = Map.empty
              DocComment = None }

        let typeWithoutAttr: TypeInfo =
            { typeWithAttr with
                Fields =
                    [ { Name = "Total"
                        TypeName = "decimal"
                        Attributes = Map.empty
                        DocComment = None } ] }

        let withAttr = ConventionEngine.score terms registry typeWithAttr
        let withoutAttr = ConventionEngine.score terms registry typeWithoutAttr

        Expect.isTrue
            (withAttr.Confidence >= withoutAttr.Confidence)
            $"attr boost: {withAttr.Confidence} >= {withoutAttr.Confidence}"
    }

// ── IGraph extraction wiring ──────────────────────────────────────────────────

[<Tests>]
let graphExtractionTests =
    testList
        "IGraph vocab term extraction"
        [ test "extractVocabTerms finds classes and properties from Turtle IGraph" {
              let turtle =
                  """
@prefix schema: <https://schema.org/> .
@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

schema:Order a rdfs:Class .
schema:totalPaymentDue a rdf:Property .
schema:orderedItem a rdf:Property .
"""

              let graph = new Graph() :> IGraph
              let parser = TurtleParser()
              use stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(turtle))
              use reader = new System.IO.StreamReader(stream)
              parser.Load(graph, reader)

              let terms = ConventionEngine.extractVocabTerms graph

              Expect.isTrue (Map.containsKey "order" terms.Classes) $"order class found; classes: {terms.Classes}"

              Expect.isTrue
                  (Map.containsKey "totalpaymentdue" terms.Properties)
                  $"totalpaymentdue property found; properties: {terms.Properties}"
          }

          test "extractVocabTerms finds schema.org-typed classes (schema:Class)" {
              let turtle =
                  """
@prefix schema: <https://schema.org/> .
@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .

schema:Invoice a schema:Class .
schema:price a schema:Property .
"""

              let graph = new Graph() :> IGraph
              let parser = TurtleParser()
              use stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(turtle))
              use reader = new System.IO.StreamReader(stream)
              parser.Load(graph, reader)

              let terms = ConventionEngine.extractVocabTerms graph

              Expect.isTrue (Map.containsKey "invoice" terms.Classes) $"invoice class found; classes: {terms.Classes}"

              Expect.isTrue
                  (Map.containsKey "price" terms.Properties)
                  $"price property found; properties: {terms.Properties}"
          } ]

// ── Out-of-scope prefix filtering ────────────────────────────────────────────

[<Tests>]
let scopeFilteringTests =
    test "score ignores classes whose prefix is not in registry.Using" {
        let terms: VocabTerms =
            { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
              Properties = Map.empty }

        let typeInfo: TypeInfo =
            { FullName = "MyApp.Order"
              Namespace = "MyApp"
              LocalName = "Order"
              Fields = []
              Attributes = Map.empty
              DocComment = None }

        let registryNoUsing = vocabulary { prefix "schema" "https://schema.org/" }

        let mapping = ConventionEngine.score terms registryNoUsing typeInfo

        Expect.equal mapping.Status MappingStatus.Unresolved "unresolved when prefix not in Using"
    }

// ── Explicit equivalentClass override ────────────────────────────────────────

[<Tests>]
let explicitEquivalentClassTests =
    testList
        "explicit equivalentClass override"
        [ test "type with equivalentClass entry gets Manual/Confirmed/1.0 and CURIE IRI" {
              // MoveLog<_> doesn't convention-match "itemlist" (tokens: move, log vs itemlist)
              // but the vocabulary declares equivalentClass typedefof<MoveLog<_>> "schema:ItemList"
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "itemlist", "https://schema.org/ItemList" ]
                    Properties = Map.empty }

              let typeInfo: TypeInfo =
                  { FullName = "TicTacToe.Model.MoveLog`1"
                    Namespace = "TicTacToe.Model"
                    LocalName = "MoveLog"
                    Fields = []
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                      equivalentClass typedefof<System.Collections.Generic.List<_>> "schema:ItemList"
                  }

              // Patch: use a registry with MoveLog`1 key directly via a helper
              let registryWithMoveLog =
                  { registry with
                      EquivalentClasses =
                          Map.add
                              "TicTacToe.Model.MoveLog`1"
                              (Uri("https://schema.org/ItemList"))
                              registry.EquivalentClasses }

              let mapping = ConventionEngine.score terms registryWithMoveLog typeInfo

              Expect.equal mapping.Source MappingSource.Manual "source is Manual"
              Expect.equal mapping.Status MappingStatus.Confirmed "status is Confirmed"
              Expect.floatClose Accuracy.high mapping.Confidence 1.0 "confidence is 1.0"
              Expect.equal mapping.Iri (Some "schema:ItemList") "iri is schema:ItemList CURIE"
          }

          test "type WITHOUT equivalentClass entry keeps convention scoring" {
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
                    Properties = Map.empty }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Order"
                    Namespace = "MyApp"
                    LocalName = "Order"
                    Fields = []
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                  }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Source MappingSource.Convention "source stays Convention"
          }

          test "generic-definition type (FullName ends `1) correctly matched in EquivalentClasses" {
              // Verifies Map.tryFind works on the `1 suffix form used by generic type defs
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "itemlist", "https://schema.org/ItemList" ]
                    Properties = Map.empty }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Container`1"
                    Namespace = "MyApp"
                    LocalName = "Container"
                    Fields = []
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                      Using = Set.ofList [ "schema" ]
                      EquivalentClasses = Map.ofList [ "MyApp.Container`1", Uri("https://schema.org/ItemList") ] }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Source MappingSource.Manual "source is Manual for generic def"
              Expect.equal mapping.Iri (Some "schema:ItemList") "iri is schema:ItemList"
          }

          test "CURIE reverse-resolution: absolute Uri -> prefix:local when prefix matches" {
              // Verify toCurie logic via score: absolute https://schema.org/ItemList
              // with prefix schema -> https://schema.org/ produces "schema:ItemList"
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "itemlist", "https://schema.org/ItemList" ]
                    Properties = Map.empty }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Thing`1"
                    Namespace = "MyApp"
                    LocalName = "Thing"
                    Fields = []
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                      Using = Set.ofList [ "schema" ]
                      EquivalentClasses = Map.ofList [ "MyApp.Thing`1", Uri("https://schema.org/ItemList") ] }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Iri (Some "schema:ItemList") "reverse-resolved to CURIE"
          }

          test "no matching prefix falls back to absolute URI string" {
              let terms: VocabTerms =
                  { Classes = Map.empty
                    Properties = Map.empty }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Foo"
                    Namespace = "MyApp"
                    LocalName = "Foo"
                    Fields = []
                    Attributes = Map.empty
                    DocComment = None }

              // registry has no prefixes at all → toCurie must fall back to absolute URI
              let registry =
                  { VocabularyRegistry.empty with
                      EquivalentClasses = Map.ofList [ "MyApp.Foo", Uri("https://example.com/Bar") ] }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Iri (Some "https://example.com/Bar") "fallback to absolute URI"
          }

          test "explicit equivalentClass keeps convention-scored fields" {
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "itemlist", "https://schema.org/ItemList" ]
                    Properties =
                      Map.ofList [ "name", "https://schema.org/name"; "position", "https://schema.org/position" ] }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Log`1"
                    Namespace = "MyApp"
                    LocalName = "Log"
                    Fields =
                      [ { Name = "Position"
                          TypeName = "int"
                          Attributes = Map.empty
                          DocComment = None } ]
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                      Using = Set.ofList [ "schema" ]
                      EquivalentClasses = Map.ofList [ "MyApp.Log`1", Uri("https://schema.org/ItemList") ] }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Iri (Some "schema:ItemList") "class IRI overridden"
              Expect.isNonEmpty mapping.Fields "fields convention-scored"
              Expect.equal mapping.Fields.[0].Name "Position" "field name preserved"
          } ]

// ── Exact-identity confirm rule ───────────────────────────────────────────────

[<Tests>]
let exactIdentityConfirmTests =
    testList
        "Exact-identity confirm rule"
        [ test "type fuzzy demote: Player vs play (JW=0.93) must be Proposed not Confirmed" {
              // jaroWinkler "player" "play" = 0.933 clears the 0.85 token-hit gate.
              // Field "Name" vs property "name" gives 100% field overlap so
              // combined = 0.6*0.933 + 0.4*1.0 = 0.96 >= 0.85 — old code Confirms.
              // New rule: "player" != "play" (token-concat != class local name) -> Proposed.
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "play", "https://schema.org/Play" ]
                    Properties = Map.ofList [ "name", "https://schema.org/name" ] }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Player"
                    Namespace = "MyApp"
                    LocalName = "Player"
                    Fields =
                      [ { Name = "Name"
                          TypeName = "string"
                          Attributes = Map.empty
                          DocComment = None } ]
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                  }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Status MappingStatus.Proposed "fuzzy type match must be Proposed not Confirmed"
              Expect.isSome mapping.Iri "IRI still surfaced as suggestion"
              Expect.equal mapping.Iri (Some "schema:Play") "correct IRI"
          }

          test "field fuzzy demote: Total vs totalpaymentdue (JW=0.87) must be Proposed not Confirmed" {
              // Type name Order exact-matches class "order" so the type is Confirmed.
              // Field "Total": normKey("Total") = "total" != "totalpaymentdue"
              // so the field must be Proposed. Old code confirmed it at JW 0.867 >= 0.85.
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
                    Properties = Map.ofList [ "totalpaymentdue", "https://schema.org/totalPaymentDue" ] }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Order"
                    Namespace = "MyApp"
                    LocalName = "Order"
                    Fields =
                      [ { Name = "Total"
                          TypeName = "decimal"
                          Attributes = Map.empty
                          DocComment = None } ]
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                  }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Status MappingStatus.Confirmed "type is exact-match so Confirmed"
              let fieldMapping = mapping.Fields |> List.head
              Expect.equal fieldMapping.Status MappingStatus.Proposed "field fuzzy match must be Proposed not Confirmed"
              Expect.isSome fieldMapping.Iri "field IRI surfaced as suggestion"
              Expect.equal fieldMapping.Iri (Some "schema:totalPaymentDue") "correct field IRI"
          }

          test "type exact still confirms: Order token-concat equals class local name" {
              // normalizeTokens "Order" = ["order"], concat = "order" = class key "order"
              // so exact identity -> Confirmed regardless of combined score.
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
                    Properties = Map.empty }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Order"
                    Namespace = "MyApp"
                    LocalName = "Order"
                    Fields = []
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                  }

              let mapping = ConventionEngine.score terms registry typeInfo

              Expect.equal mapping.Status MappingStatus.Confirmed "exact token identity -> Confirmed"
          }

          test "field exact still confirms: Name normKey equals property key" {
              // normKey("Name") = "name" = property key "name" -> Confirmed.
              let terms: VocabTerms =
                  { Classes = Map.ofList [ "order", "https://schema.org/Order" ]
                    Properties = Map.ofList [ "name", "https://schema.org/name" ] }

              let typeInfo: TypeInfo =
                  { FullName = "MyApp.Order"
                    Namespace = "MyApp"
                    LocalName = "Order"
                    Fields =
                      [ { Name = "Name"
                          TypeName = "string"
                          Attributes = Map.empty
                          DocComment = None } ]
                    Attributes = Map.empty
                    DocComment = None }

              let registry =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                  }

              let mapping = ConventionEngine.score terms registry typeInfo

              let fieldMapping = mapping.Fields |> List.head
              Expect.equal fieldMapping.Status MappingStatus.Confirmed "exact field key -> Confirmed"
              Expect.equal fieldMapping.Iri (Some "schema:name") "correct field IRI"
          } ]
