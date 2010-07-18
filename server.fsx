#r "System.Core.dll"
#r "System.Net.dll"
#r "System.Web.dll"
open System
open System.IO
open System.Net
open System.Web
open System.Web.Hosting

type HttpListenerWorkerRequest(ctx:HttpListenerContext) =
  inherit HttpWorkerRequest()
  override this.EndOfRequest() =
    ctx.Response.OutputStream.Close()
    ctx.Response.Close()
  override this.GetUriPath() = ctx.Request.Url.LocalPath
  override this.GetQueryString() =
    let idx = ctx.Request.RawUrl.IndexOf("?")
    if idx = -1 then "" else ctx.Request.RawUrl.Substring(idx + 1)
  override this.GetRawUrl() = ctx.Request.RawUrl
  override this.GetHttpVerbName() = ctx.Request.HttpMethod
  override this.GetHttpVersion() = sprintf "HTTP/%d.%d" ctx.Request.ProtocolVersion.Major ctx.Request.ProtocolVersion.Minor
  override this.GetRemoteAddress() = ctx.Request.RemoteEndPoint.Address.ToString()
  override this.GetRemotePort() = ctx.Request.RemoteEndPoint.Port
  override this.GetLocalAddress() = ctx.Request.LocalEndPoint.Address.ToString()
  override this.GetLocalPort() = ctx.Request.LocalEndPoint.Port
  override this.SendStatus(statusCode, statusDescription) =
    ctx.Response.StatusCode <- statusCode
    ctx.Response.StatusDescription <- statusDescription
  override this.SendKnownResponseHeader(index, value) =
    ctx.Response.Headers.[HttpWorkerRequest.GetKnownResponseHeaderName(index)] <- value
  override this.SendUnknownResponseHeader(name, value) =
    ctx.Response.Headers.[name] <- value
  override this.SendResponseFromMemory(data, length) =
    ctx.Response.OutputStream.Write(data, 0, length)
  override this.SendResponseFromFile(filename:string, offset:int64, length:int64) : unit =
    use f = File.OpenRead(filename)
    f.Seek(offset, SeekOrigin.Begin) |> ignore
    let buf = Array.zeroCreate 1024
    let read = ref length
    while !read > 0L do
      let bytesRead = f.Read (buf, offset = 0, count = buf.Length)
      ctx.Response.OutputStream.Write(buf, 0, bytesRead)
      read := !read - (int64 bytesRead)
  override this.SendResponseFromFile(handle:nativeint, offset:int64, length:int64) : unit =
    failwith "Not supported."
  override this.FlushResponse(finalFlush) =
    ctx.Response.OutputStream.Flush()

type Marshaller() =
  inherit MarshalByRefObject()
  let listener = new HttpListener()
  do listener.Prefixes.Add("http://+:8099")
  member this.Start() =
    let processor = async {
      try
        while true do
          let! context = Async.FromBeginEnd(listener.BeginGetContext, listener.EndGetContext)
          printfn "requesting %O" context.Request.Url
          async { HttpRuntime.ProcessRequest(HttpListenerWorkerRequest context) } |> Async.Start
      with
        :? IOException as ioe -> ()
      }
    listener.Start()
    Async.Start processor
  member this.Stop() = listener.Stop()
  override this.InitializeLifetimeService() = null

let runServer virtualPath physicalPath =
  let host = ApplicationHost.CreateApplicationHost(typeof<Marshaller>, virtualPath, physicalPath) :?> Marshaller
  host.Start()
  { new IDisposable with
      member this.Dispose() = host.Stop() }

let (|Arg|_|) arg (s:string) =
  if s.StartsWith(arg) then Some(s.Substring(arg.Length)) else None
let parse args =
  ((null, null), args) ||> Array.fold(fun (dir, name) arg ->
    match arg with
    | Arg "-dir:" dir -> (dir, name)
    | Arg "-name:" name -> (dir, name)
    | _ -> (dir, name)
  )

[<EntryPoint>]
let main args =
  let (dir, name) = parse args
  let handle = runServer name dir
  printfn "Press any key to stop ..."
  Console.ReadKey() |> ignore
  handle.Dispose()
  0