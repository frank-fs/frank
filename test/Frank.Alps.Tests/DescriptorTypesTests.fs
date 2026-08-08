module Frank.Alps.Tests.DescriptorTypesTests

open Expecto
open Frank.Alps

let private emptyDescriptor (id: string) : Descriptor =
    { Id = id
      Name = None
      Type = DescriptorType.Semantic
      Def = None
      Doc = None
      Ext = []
      InheritsFrom = None
      Rt = None
      From = []
      Guard = None
      Targets = []
      Rel = None
      Tag = []
      Link = []
      Descriptors = [] }

[<Tests>]
let tests =
    testList
        "DescriptorTypes"
        [ test "a Descriptor can nest itself via Rt without a compiler error" {
              let target = emptyDescriptor "target"
              let d = { emptyDescriptor "source" with Rt = Some target }
              Expect.equal d.Rt.Value.Id "target" ""
          }

          test "a Descriptor can nest itself via Descriptors without a compiler error" {
              let child = emptyDescriptor "child"
              let d = { emptyDescriptor "parent" with Descriptors = [ child ] }
              Expect.equal d.Descriptors.Length 1 ""
          }

          test "DescriptorRef.Local holds a Descriptor value directly" {
              let target = emptyDescriptor "target"
              let d = { emptyDescriptor "source" with InheritsFrom = Some(DescriptorRef.Local target) }

              match d.InheritsFrom with
              | Some(DescriptorRef.Local t) -> Expect.equal t.Id "target" ""
              | _ -> failwith "expected Local"
          }

          test "DescriptorRef.External holds a bare Uri" {
              let uri = System.Uri "https://example.org/other-profile#thing"
              let d = { emptyDescriptor "source" with InheritsFrom = Some(DescriptorRef.External uri) }

              match d.InheritsFrom with
              | Some(DescriptorRef.External u) -> Expect.equal u uri ""
              | _ -> failwith "expected External"
          }

          test "a Descriptor can hold a StateGuard via Guard without a compiler error" {
              let a = emptyDescriptor "a"
              let d = { emptyDescriptor "t" with Guard = Some(StateGuard.State a) }
              match d.Guard with
              | Some(StateGuard.State s) -> Expect.equal s.Id "a" ""
              | _ -> failwith "expected State"
          }

          test "StateGuard nests: All/Any/Not wrap StateGuard, not Descriptor" {
              let a, b = emptyDescriptor "a", emptyDescriptor "b"
              let g = StateGuard.All [ StateGuard.State a; StateGuard.Any [ StateGuard.State b; StateGuard.Not(StateGuard.State a) ] ]
              match g with
              | StateGuard.All [ StateGuard.State _; StateGuard.Any [ StateGuard.State _; StateGuard.Not(StateGuard.State _) ] ] -> ()
              | _ -> failwith "expected nested All/Any/Not"
          }

          test "a Descriptor can hold TransitionTarget list via Targets" {
              let a = emptyDescriptor "region"
              let d = { emptyDescriptor "t" with Targets = [ TransitionTarget.EnterState a; TransitionTarget.History a; TransitionTarget.DeepHistory a ] }
              Expect.equal d.Targets.Length 3 ""
          } ]
