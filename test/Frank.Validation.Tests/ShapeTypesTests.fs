module Frank.Validation.Tests.ShapeTypesTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation

[<Tests>]
let tests =
    testList
        "ShapeTypes"
        [ test "NonEmptyList.ofList: None on empty, Some on non-empty; toList round-trips" {
              Expect.isNone (NonEmptyList.ofList ([]: int list)) "empty -> None"
              let nel = NonEmptyList.ofList [ 1; 2; 3 ] |> Option.get
              Expect.equal nel.Head 1 "head"
              Expect.equal nel.Tail [ 2; 3 ] "tail"
              Expect.equal (NonEmptyList.toList nel) [ 1; 2; 3 ] "round-trip"
          }

          test "XsdDatatype cases are unambiguous when RequireQualifiedAccess" {
              let d: XsdDatatype = XsdDatatype.Integer
              Expect.equal d XsdDatatype.Integer "no Xsd-prefixed case names"
          }

          test "PropertyPath: recursive cases construct (predicate, inverse, sequence, alternative, cardinality)" {
              let p1 = PropertyPath.Predicate(Uri "https://schema.org/knows")
              let p2 = PropertyPath.Inverse p1
              let p3 = PropertyPath.Sequence { Head = p1; Tail = [ p2 ] }
              let p4 = PropertyPath.Alternative { Head = p1; Tail = [ p2 ] }
              let p5 = PropertyPath.ZeroOrMore p1
              let p6 = PropertyPath.OneOrMore p1
              let p7 = PropertyPath.ZeroOrOne p1
              Expect.equal p2 (PropertyPath.Inverse(PropertyPath.Predicate(Uri "https://schema.org/knows"))) "inverse"

              Expect.equal
                  (match p3 with
                   | PropertyPath.Sequence n -> NonEmptyList.toList n |> List.length
                   | _ -> 0)
                  2
                  "sequence length"

              Expect.equal
                  (match p4 with
                   | PropertyPath.Alternative n -> NonEmptyList.toList n |> List.length
                   | _ -> 0)
                  2
                  "alternative length"

              ignore (p5, p6, p7)
          }

          test "PropertyShapeSpec and NodeShapeSpec are plain records with the designed fields" {
              let p: PropertyShapeSpec =
                  { Path = PropertyPath.Predicate(Uri "https://schema.org/position")
                    Constraints =
                      [ PropertyConstraint.Datatype XsdDatatype.Integer
                        PropertyConstraint.MinCount 1 ]
                    Severity = None
                    Message = None }

              let n: NodeShapeSpec =
                  { Targets = [ TargetSpec.Class(Uri "https://schema.org/MoveAction") ]
                    Properties = [ p ]
                    Closed = false
                    IgnoredProperties = []
                    Severity = None
                    Message = None }

              Expect.equal n.Properties.Length 1 "one property shape"
              Expect.equal n.Targets [ TargetSpec.Class(Uri "https://schema.org/MoveAction") ] "targets"
          }

          test "NodeShapeSpec.Targets may be empty -- a shape referenced only via sh:node" {
              let n: NodeShapeSpec =
                  { Targets = []
                    Properties = []
                    Closed = false
                    IgnoredProperties = []
                    Severity = None
                    Message = None }

              Expect.isEmpty n.Targets "no explicit target required"
          }

          test "ShapeDecl is a total DU over RecordShape | EnumShape | And | Or | Not | Xone" {
              let record =
                  ShapeDecl.RecordShape
                      { Targets = [ TargetSpec.Class(Uri "https://schema.org/Person") ]
                        Properties = []
                        Closed = false
                        IgnoredProperties = []
                        Severity = None
                        Message = None }

              let enum =
                  ShapeDecl.EnumShape(
                      Uri "https://schema.org/GameStatusType",
                      { Head = Uri "https://schema.org/ActiveActionStatus"
                        Tail = [] }
                  )

              let combined = ShapeDecl.And { Head = record; Tail = [ enum ] }
              let negated = ShapeDecl.Not record
              let xor = ShapeDecl.Xone { Head = record; Tail = [ enum ] }
              let alt = ShapeDecl.Or { Head = record; Tail = [ enum ] }

              let describe =
                  function
                  | ShapeDecl.RecordShape _ -> "record"
                  | ShapeDecl.EnumShape _ -> "enum"
                  | ShapeDecl.And _ -> "and"
                  | ShapeDecl.Or _ -> "or"
                  | ShapeDecl.Not _ -> "not"
                  | ShapeDecl.Xone _ -> "xone"

              Expect.equal (describe record) "record" "record case"
              Expect.equal (describe enum) "enum" "enum case"
              Expect.equal (describe combined) "and" "and case"
              Expect.equal (describe negated) "not" "not case"
              Expect.equal (describe xor) "xone" "xone case"
              Expect.equal (describe alt) "or" "or case"
          }

          test "PropertyConstraint.Node is recursive -- a property can require conformance to another ShapeDecl" {
              let inner =
                  ShapeDecl.RecordShape
                      { Targets = []
                        Properties = []
                        Closed = false
                        IgnoredProperties = []
                        Severity = None
                        Message = None }

              let c = PropertyConstraint.Node inner
              Expect.equal c (PropertyConstraint.Node inner) "recursive constraint constructs"
          }

          test "SparqlConstraint carries author-supplied query text, message, and prefixes" {
              let sc: SparqlConstraint =
                  { Query = "ASK { $this a <https://schema.org/Person> }"
                    Message = Some "must be a Person"
                    Prefixes = [ "schema", "https://schema.org/" ] }

              Expect.stringContains sc.Query "ASK" "query text preserved"
          } ]
