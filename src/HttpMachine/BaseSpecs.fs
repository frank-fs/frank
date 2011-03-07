module BaseSpecs
open System
open NUnit.Framework

let inline (==) (actual:#obj) (expected:#obj) = Assert.AreEqual(expected, actual)
let inline (!=) (actual:#obj) (expected:#obj) = Assert.AreNotEqual(expected, actual)
let inline (<->) (actual:#obj) expected = Assert.IsInstanceOf(expected, actual)
let inline (<!>) (actual:#obj) expected = Assert.IsNotInstanceOf(expected, actual)
let ``is null`` anObject = Assert.IsNull(anObject)
let ``is not null`` anObject = Assert.NotNull(anObject)

let inline (===) actual expected =
  expected = actual
  |> (fun res -> Assert.IsTrue(res, sprintf "Expected: %A\r\n  But was: %A." expected actual))
let inline (!==) actual expected =
  expected = actual
  |> (fun res -> Assert.IsTrue(res, sprintf "Did not expect: %A\r\n  But was: %A." expected actual))

let inline (===>) (actual: (_ * ArraySegment<_>) option) (expected: (_ * ArraySegment<_>) option) =
  if actual.IsNone && expected.IsNone then actual == expected
  else
    let avalue, asegment = actual |> Option.get
    let evalue, esegment = expected |> Option.get
    avalue === evalue
    asegment.Array == esegment.Array
    asegment.Offset == esegment.Offset
    asegment.Count == esegment.Count
