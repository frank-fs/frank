module UriParserSpecs
open System
open HttpMachine.UriParser
open NUnit.Framework
open BaseSpecs

let (!!) str = List.ofSeq str
let (!!!) str = ArraySegment<_>(str)
let (!!+) str offset = ArraySegment<_>(str, offset, str.Length - offset)
let (!+) str = !!+str 1

let ``scheme cases`` = [|
  [| box "http"B; Some(Scheme(!!"http"B), !!+"http"B 4) |> box |]
  [| box "http://boo"B; Some(Scheme(!!"http"B), !!+"http://boo"B 4) |> box |]
  [| box "https"B; Some(Scheme(!!"https"B), !!+"https"B 5) |> box |]
  [| box "https://boo"B; Some(Scheme(!!"https"B), !!+"https://boo"B 5) |> box |]
  [| box "urn"B; Some(Scheme(!!"urn"B), !!+"urn"B 3) |> box |]
  [| box "ftp"B; Some(Scheme(!!"ftp"B), !!+"ftp"B 3) |> box |]
  [| box "mailto"B; Some(Scheme(!!"mailto"B), !!+"mailto"B 6) |> box |]
  [| box "mailto:ryan@owin.org"B; Some(Scheme(!!"mailto"B), !!+"mailto:ryan@owin.org"B 6) |> box |]
|]

[<Test>]
[<TestCaseSource("scheme cases")>]
let ``Given a scheme, the scheme parser should return a Scheme containing the value``(input: byte[], expected: (UriPart * ArraySegment<byte>) option) =
  scheme !!!input ===> expected

