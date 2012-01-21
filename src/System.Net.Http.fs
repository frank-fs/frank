namespace System.Net.Http

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

[<AutoOpen>]
module Extensions =
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
  