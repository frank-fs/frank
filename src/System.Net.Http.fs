namespace System.Net.Http

open System.Net.Http
open System.Net.Http.Formatting

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

type SimpleObjectContent<'a>(body: 'a, formatter: MediaTypeFormatter) =
  inherit HttpContent()
  override x.SerializeToStreamAsync(stream, context) =
    let mediaType = formatter.SupportedMediaTypes |> Seq.head
    formatter.WriteToStreamAsync(typeof<'a>, body, stream, x.Headers, FormatterContext(mediaType, false), context)
  override x.TryComputeLength(length) =
    length <- -1L
    false

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
  
  let addHeaders headers response = headers response; response
  
  // Responding with the actual types can get a bit noisy with the long type names and required
  // type cast to `HttpResponseMessage` (since most responses will include a typed body).
  // The `Reply` and `ReplyTo` methods simplify this and also accept an
  // `HttpResponseHeadersBuilder` to allow easy composition and inclusion of headers.
  // Finally, several overloads take either a `request`, a `body`, or both.
  // The `request` helps manage content negotiation, while the `body` provides the content
  // for the response message.
  type HttpResponseMessage with

    static member ReplyTo(request) =
      new HttpResponseMessage(Content = HttpContent.Empty, RequestMessage = request)
    static member ReplyTo(request, statusCode) =
      new HttpResponseMessage(statusCode, Content = HttpContent.Empty, RequestMessage = request)
    static member ReplyTo(request, headers) =
      new HttpResponseMessage(Content = HttpContent.Empty, RequestMessage = request)
      |> addHeaders headers
    static member ReplyTo(request, statusCode, headers) =
      new HttpResponseMessage(statusCode, Content = HttpContent.Empty, RequestMessage = request)
      |> addHeaders headers

    static member ReplyTo(request, body) =
      new HttpResponseMessage(Content = body, RequestMessage = request)
    static member ReplyTo(request, body, statusCode: HttpStatusCode) =
      new HttpResponseMessage(statusCode, Content = body, RequestMessage = request)
    static member ReplyTo(request, body, headers) =
      new HttpResponseMessage(Content = body, RequestMessage = request)
      |> addHeaders headers
    static member ReplyTo(request, body, statusCode: HttpStatusCode, headers) =
      new HttpResponseMessage(statusCode, Content = body, RequestMessage = request)
      |> addHeaders headers

    static member ReplyTo(request, body: 'a) =
      new HttpResponseMessage<_>(body, RequestMessage = request)
      :> HttpResponseMessage
    static member ReplyTo(request, body: 'a, statusCode: HttpStatusCode) =
      new HttpResponseMessage<_>(body, statusCode, RequestMessage = request)
      :> HttpResponseMessage
    static member ReplyTo(request, body: 'a, formatters: seq<MediaTypeFormatter>) =
      new HttpResponseMessage<_>(body, formatters, RequestMessage = request)
      :> HttpResponseMessage
    static member ReplyTo(request, body: 'a, statusCode, formatters) =
      new HttpResponseMessage<_>(body, statusCode, formatters, RequestMessage = request)
      :> HttpResponseMessage
    static member ReplyTo(request, body: 'a, headers) =
      new HttpResponseMessage<_>(body, RequestMessage = request)
      :> HttpResponseMessage
      |> addHeaders headers
    static member ReplyTo(request, body: 'a, statusCode: HttpStatusCode, headers) =
      new HttpResponseMessage<_>(body, statusCode, RequestMessage = request)
      :> HttpResponseMessage
      |> addHeaders headers
    static member ReplyTo(request, body: 'a, statusCode, formatters, headers) =
      new HttpResponseMessage<_>(body, statusCode, formatters, RequestMessage = request)
      :> HttpResponseMessage
      |> addHeaders headers
  
  #if DEBUG
  open System.Json
  open ImpromptuInterface.FSharp
  open NUnit.Framework
  open Swensen.Unquote.Assertions

  [<Test>]
  let ``test respond without body``() =
    let response = HttpResponseMessage.ReplyTo(new HttpRequestMessage())
    test <@ response.StatusCode = HttpStatusCode.OK @>
    test <@ response.Content = HttpContent.Empty @>
  
  [<Test>]
  let ``test respond with StringContent``() =
    let body = "Howdy"
    let response = HttpResponseMessage.ReplyTo(new HttpRequestMessage(), new StringContent(body))
    test <@ response.StatusCode = HttpStatusCode.OK @>
    test <@ response.Content.ReadAsStringAsync().Result = body @>

  [<Test>]
  let ``test respond with negotiated body``() =
    let body = "Howdy"
    let response = HttpResponseMessage.ReplyTo(new HttpRequestMessage(), body)
    test <@ response.StatusCode = HttpStatusCode.OK @>
    test <@ response.Content.ReadAsStringAsync().Result = "<?xml version=\"1.0\" encoding=\"utf-8\"?><string>Howdy</string>" @>
  #endif
