module Frank.Validation.Tests.ShapeSpecTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

[<Tests>]
let tests =
    testList
        "ShapeSpecFunctions"
        [ test "ofPath seeds an empty, unconstrained PropertyShapeSpec" {
              let p = ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
              Expect.isEmpty p.Constraints "no constraints yet"
              Expect.isNone p.Severity "no severity yet"
              Expect.isNone p.Message "no message yet"
          }

          test "addConstraint appends, preserving order, and is the basis for every per-constraint helper" {
              let p =
                  ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                  |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer)
                  |> addConstraint (PropertyConstraint.MinCount 1)
                  |> addConstraint (PropertyConstraint.MaxCount 1)

              Expect.equal
                  p.Constraints
                  [ PropertyConstraint.Datatype XsdDatatype.Integer
                    PropertyConstraint.MinCount 1
                    PropertyConstraint.MaxCount 1 ]
                  "constraints append in call order"
          }

          test "recordShape builds a ShapeDecl.RecordShape with the given targets and properties, defaults otherwise" {
              let prop =
                  ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                  |> addConstraint (PropertyConstraint.MinCount 1)

              let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ prop ]

              match decl with
              | ShapeDecl.RecordShape n ->
                  Expect.equal n.Targets [ TargetSpec.Class(Uri "https://schema.org/MoveAction") ] "targets"
                  Expect.equal n.Properties [ prop ] "properties"
                  Expect.isFalse n.Closed "not closed by default"
                  Expect.isEmpty n.IgnoredProperties "no ignored properties by default"
              | other -> failtestf "expected RecordShape, got %A" other
          }

          test "recordShape with empty targets is valid -- for shapes referenced only via sh:node" {
              let decl = recordShape [] []

              match decl with
              | ShapeDecl.RecordShape n -> Expect.isEmpty n.Targets "empty targets accepted"
              | other -> failtestf "expected RecordShape, got %A" other
          }

          test "enumShape builds a ShapeDecl.EnumShape with a guaranteed non-empty case list" {
              let decl =
                  enumShape
                      (Uri "https://schema.org/GameStatusType")
                      (Uri "https://schema.org/ActiveActionStatus")
                      [ Uri "https://schema.org/CompletedActionStatus" ]

              match decl with
              | ShapeDecl.EnumShape(targetClass, cases) ->
                  Expect.equal targetClass (Uri "https://schema.org/GameStatusType") "target class"

                  Expect.equal
                      (NonEmptyList.toList cases)
                      [ Uri "https://schema.org/ActiveActionStatus"
                        Uri "https://schema.org/CompletedActionStatus" ]
                      "cases"
              | other -> failtestf "expected EnumShape, got %A" other
          }

          test "targetClass is sugar for a single-element TargetSpec.Class list" {
              Expect.equal
                  (targetClass (Uri "https://schema.org/Person"))
                  [ TargetSpec.Class(Uri "https://schema.org/Person") ]
                  "single-element list"
          } ]
