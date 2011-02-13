module OwinSpecs
open System
open System.Collections.Generic
open System.IO
open Frack
open Frack.Collections
open Request
open NUnit.Framework
open BaseSpecs

// NOTE: I am not testing every function in the Request module b/c most will be used through Action delegates.
// NOTE: OWIN uses Action delegates, but Frack uses F#'s Async underneath. The OWIN tests should be sufficient for the F# Asyncs.

[<Test>]
let ``Chunking an empty stream should return an ArraySegment with a count of 0``() =
  use stream = new MemoryStream(0)

  let cb = chunk stream

  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 0)), Action<_>(fun e -> ()))


[<Test>]
let ``Chunking a stream of size 1024 should return one chunk with 1024 bytes and another with none``() =
  let buffer = Array.zeroCreate 1024
  use stream = new MemoryStream(buffer)

  let cb = chunk stream

  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 1024)), Action<_>(fun e -> ()))
  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 0)), Action<_>(fun e -> ()))


[<Test>]
let ``Chunking stream of size 2048 should return two 1024 byte segments and one zero byte segment``() =
  let buffer = Array.zeroCreate 2048
  use stream = new MemoryStream(buffer)

  let cb = chunk stream

  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 1024)), Action<_>(fun e -> ()))
  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 1024)), Action<_>(fun e -> ()))
  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 0)), Action<_>(fun e -> ()))


[<Test>]
let ``Chunking a stream of size 1048 should return one 1024 byte segment, one 24 byte segment, and one zero byte segment``() =
  let buffer = Array.zeroCreate 1048
  use stream = new MemoryStream(buffer)

  let cb = chunk stream

  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 1024)), Action<_>(fun e -> ()))
  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 24)), Action<_>(fun e -> ()))
  cb.Invoke(Action<_>(fun ch -> Assert.IsTrue(ch.Count = 0)), Action<_>(fun e -> ()))


[<Test>]
let ``Reading the body of the request should return an empty list``() =
  use stream = new MemoryStream(0)
  let requestBody = chunk stream
  let request = new Dictionary<string, obj>()
  request?RequestBody <- requestBody

  let result = readBody request |> Async.RunSynchronously

  Assert.AreEqual(0, result.Length)


[<Test>]
let ``Reading the body of the request should return a list of one ArraySegment<byte>``() =
  let buffer = "Hello, world!"B
  use stream = new MemoryStream(buffer)
  let requestBody = chunk stream
  let request = new Dictionary<string, obj>()
  request?RequestBody <- requestBody

  let result = readBody request |> Async.RunSynchronously

  Assert.AreEqual(1, result.Length)
  Assert.AreEqual(buffer, result.Head.Array)


[<Test>]
let ``Reading the body of the request to its end should return an empty byte[]``() =
  use stream = new MemoryStream(0)
  let requestBody = chunk stream
  let request = new Dictionary<string, obj>()
  request?RequestBody <- requestBody

  readToEndWithContinuations(request, Action<_>(fun bs -> Assert.AreEqual(0, bs.Length)), Action<_>(fun e -> ()))


[<Test>]
let ``Reading the body of the request to its end should return a byte[] containing the contents of the request body``() =
  let buffer = "Hello, world!"B
  use stream = new MemoryStream(buffer)
  let requestBody = chunk stream
  let request = new Dictionary<string, obj>()
  request?RequestBody <- requestBody

  readToEndWithContinuations(request, Action<_>(fun bs -> Assert.AreEqual(buffer, bs)), Action<_>(fun e -> ()))