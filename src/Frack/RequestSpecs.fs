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
  let asyncRead = chunk stream
  async {
    let! cb = asyncRead
    Assert.IsTrue(cb.Count = 0) } |> Async.RunSynchronously

[<Test>]
let ``Chunking a stream of size 1024 should return one chunk with 1024 bytes and another with none``() =
  let buffer = Array.zeroCreate 1024
  use stream = new MemoryStream(buffer)
  let asyncRead = chunk stream
  async {
    let! cb1 = asyncRead
    let! cb2 = asyncRead
    Assert.IsTrue(cb1.Count = 1024)
    Assert.IsTrue(cb2.Count = 0) } |> Async.RunSynchronously

[<Test>]
let ``Chunking stream of size 2048 should return two 1024 byte segments and one zero byte segment``() =
  let buffer = Array.zeroCreate 2048
  use stream = new MemoryStream(buffer)
  let asyncRead = chunk stream
  async {
    let! cb1 = asyncRead
    let! cb2 = asyncRead
    let! cb3 = asyncRead
    Assert.IsTrue(cb1.Count = 1024)
    Assert.IsTrue(cb2.Count = 1024)
    Assert.IsTrue(cb3.Count = 0) } |> Async.RunSynchronously

[<Test>]
let ``Chunking a stream of size 1048 should return one 1024 byte segment, one 24 byte segment, and one zero byte segment``() =
  let buffer = Array.zeroCreate 1048
  use stream = new MemoryStream(buffer)
  let asyncRead = chunk stream
  async {
    let! cb1 = asyncRead
    let! cb2 = asyncRead
    let! cb3 = asyncRead
    Assert.IsTrue(cb1.Count = 1024)
    Assert.IsTrue(cb2.Count = 24)
    Assert.IsTrue(cb3.Count = 0) } |> Async.RunSynchronously

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
  async {
    let! bs = readToEnd request
    Assert.AreEqual(0, bs.Length) } |> Async.RunSynchronously

[<Test>]
let ``Reading the body of the request to its end should return a byte[] containing the contents of the request body``() =
  let buffer = "Hello, world!"B
  use stream = new MemoryStream(buffer)
  let requestBody = chunk stream
  let request = new Dictionary<string, obj>()
  request?RequestBody <- requestBody
  async {
    let! bs = readToEnd request
    Assert.AreEqual(13, bs.Length) } |> Async.RunSynchronously