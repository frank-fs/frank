module Frack.UriSpecs
open System
open NUnit.Framework
open BaseSpecs

let uriCases = [|
  [|null;"/";""|]
  [|"";"/";""|]
  [|"/";"/";""|]
  [|"/something";"/something";""|]
  [|"/something/";"/something";""|]
  [|"/something/awesome";"/something/awesome";""|]
  [|"/something/awesome/";"/something/awesome";""|]
  [|"/something/awesome?and=brilliant";"/something/awesome";"and=brilliant"|]
  [|"/something/awesome/?and=brilliant";"/something/awesome";"and=brilliant"|]
|]

[<Test>]
[<TestCaseSource("uriCases")>]
let ``Splitting the uri`` (uri, path:string, queryString:string) = (SplitUri uri) == (path, queryString)