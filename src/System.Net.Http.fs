(* # F# Extensions to System.Net.Http

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2012, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
namespace System.Net.Http

open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Threading.Tasks

type EmptyContent() =
  inherit HttpContent()
  override x.SerializeToStreamAsync(stream, context) =
    let tcs = new TaskCompletionSource<_>(TaskCreationOptions.None)
    tcs.SetResult(())
    tcs.Task :> Task
  override x.TryComputeLength(length) =
    length <- 0L
    true
  override x.Equals(other) =
    other.GetType() = typeof<EmptyContent>
  override x.GetHashCode() = hash x

type AsyncHandler =
  inherit DelegatingHandler
  val AsyncSend : HttpRequestMessage -> Async<HttpResponseMessage>
  new (f, inner) = { inherit DelegatingHandler(inner); AsyncSend = f }
  new (f) = { inherit DelegatingHandler(); AsyncSend = f }
  override x.SendAsync(request, cancellationToken) =
    Async.StartAsTask(x.AsyncSend request, cancellationToken = cancellationToken)

[<AutoOpen>]
module Extensions =
  open System.Net
  open System.Net.Http
  open System.Net.Http.Headers

  let private emptyContent = new EmptyContent() :> HttpContent

  type HttpContent with
    static member Empty = emptyContent
    member x.AsyncReadAs<'a>() = Async.AwaitTask <| x.ReadAsAsync<'a>()
    member x.AsyncReadAs<'a>(formatters) = Async.AwaitTask <| x.ReadAsAsync<'a>(formatters)
    member x.AsyncReadAs(type') = Async.AwaitTask <| x.ReadAsAsync(type')
    member x.AsyncReadAs(type', formatters) = Async.AwaitTask <| x.ReadAsAsync(type', formatters)
    member x.AsyncReadAsByteArray() = Async.AwaitTask <| x.ReadAsByteArrayAsync()
    member x.AsyncReadAsHttpRequestMessage() = Async.AwaitTask <| x.ReadAsHttpRequestMessageAsync()
    member x.AsyncReadAsHttpResponseMessage() = Async.AwaitTask <| x.ReadAsHttpResponseMessageAsync()
    member x.AsyncReadAsMultipart() = Async.AwaitTask <| x.ReadAsMultipartAsync()
    member x.AsyncReadAsMultipart(streamProvider) = Async.AwaitTask <| x.ReadAsMultipartAsync(streamProvider)
    member x.AsyncReadAsMultipart(streamProvider, bufferSize) = Async.AwaitTask <| x.ReadAsMultipartAsync(streamProvider, bufferSize)
    member x.AsyncReadAsStream() = Async.AwaitTask <| x.ReadAsStreamAsync()
    member x.AsyncReadAsString() = Async.AwaitTask <| x.ReadAsStringAsync()
