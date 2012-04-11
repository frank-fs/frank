namespace System.Net.Http

open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers

type EmptyContent() =
  inherit HttpContent()
  override x.SerializeToStreamAsync(stream, context) =
    System.Threading.Tasks.Task.Factory.StartNew(fun () -> ())
  override x.TryComputeLength(length) =
    length <- 0L
    true
  override x.Equals(other) =
    other.GetType() = typeof<EmptyContent>
  override x.GetHashCode() = hash x

type SimpleObjectContent<'a>(body: 'a, mediaType: string, formatter: MediaTypeFormatter) as x =
  inherit HttpContent()
  do x.Headers.ContentType <- MediaTypeHeaderValue(mediaType)
  override x.SerializeToStreamAsync(stream, context) =
    formatter.WriteToStreamAsync(typeof<'a>, body, stream, x.Headers, FormatterContext(x.Headers.ContentType, false), context)
  override x.TryComputeLength(length) =
    length <- -1L
    false

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

  type HttpContent with
    static member Empty = new EmptyContent() :> HttpContent
    member x.AsyncReadAs<'a>() = Async.AwaitTask <| x.ReadAsAsync<'a>()
    member x.AsyncReadAs<'a>(formatters) = Async.AwaitTask <| x.ReadAsAsync<'a>(formatters)
    member x.AsyncReadAs(type') = Async.AwaitTask <| x.ReadAsAsync(type')
    member x.AsyncReadAs(type', formatters) = Async.AwaitTask <| x.ReadAsAsync(type', formatters)
    member x.AsyncReadAsByteArray() = Async.AwaitTask <| x.ReadAsByteArrayAsync()
    member x.AsyncReadAsMultipart() = Async.AwaitTask <| x.ReadAsMultipartAsync()
    member x.AsyncReadAsMultipart(streamProvider) = Async.AwaitTask <| x.ReadAsMultipartAsync(streamProvider)
    member x.AsyncReadAsMultipart(streamProvider, bufferSize) = Async.AwaitTask <| x.ReadAsMultipartAsync(streamProvider, bufferSize)
    member x.AsyncReadAsOrDefault<'a>() = Async.AwaitTask <| x.ReadAsOrDefaultAsync<'a>()
    member x.AsyncReadAsOrDefault<'a>(formatters) = Async.AwaitTask <| x.ReadAsOrDefaultAsync<'a>(formatters)
    member x.AsyncReadAsOrDefault(type') = Async.AwaitTask <| x.ReadAsOrDefaultAsync(type')
    member x.AsyncReadAsOrDefault(type', formatters) = Async.AwaitTask <| x.ReadAsOrDefaultAsync(type', formatters)
    member x.AsyncReadAsStream() = Async.AwaitTask <| x.ReadAsStreamAsync()
    member x.AsyncReadAsString() = Async.AwaitTask <| x.ReadAsStringAsync()
