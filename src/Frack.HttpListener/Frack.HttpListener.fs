namespace Frack.Hosting

[<System.Runtime.CompilerServices.Extension>]
module HttpListener =
  open System
  open System.Collections.Generic
  open System.Net
  open System.Web
  open Frack
  open Frack.Collections
  open Frack.Hosting.SystemWeb

  [<System.Runtime.CompilerServices.Extension>]
  [<Microsoft.FSharp.Core.CompiledName("ToContextBase")>]
  let toContextBase(context:HttpListenerContext) =
    let request = context.Request
    let response = context.Response
    { new HttpContextBase() with
        override this.Request =
          { new HttpRequestBase() with
              override this.AcceptTypes = request.AcceptTypes
              //override this.Cookies = request.Cookies
              override this.QueryString = request.QueryString
              override this.ContentEncoding = request.ContentEncoding
              override this.ContentType = request.ContentType
              override this.ContentLength = int request.ContentLength64
              override this.Headers = request.Headers
              override this.HttpMethod = request.HttpMethod
              override this.InputStream = request.InputStream
              override this.RawUrl = request.RawUrl
              override this.Url = request.Url }
        override this.Response =
          { new HttpResponseBase() with
              override this.AddHeader(name, value) = response.AddHeader(name, value)
              //override this.AppendCookie(cookie) = response.AppendCookie(cookie)
              override this.AppendHeader(name, value) = response.AppendHeader(name, value)
              override this.Close() = response.Close()
              override this.ContentEncoding
                with get() = response.ContentEncoding
                and set(encoding) = response.ContentEncoding <- encoding
              override this.ContentType
                with get() = response.ContentType
                and set(contentType) = response.ContentType <- contentType
              //override this.Cookies = response.Cookies
              //override this.Headers = response.Headers
              override this.StatusCode
                with get() = response.StatusCode
                and set(status) = response.StatusCode <- status
              override this.StatusDescription
                with get() = response.StatusDescription
                and set(status) = response.StatusDescription <- status
              override this.OutputStream = response.OutputStream }
        override this.User = context.User }

  type HttpListenerContext with
    member context.ToContextBase() = toContextBase context

  [<System.Runtime.CompilerServices.Extension>]
  [<Microsoft.FSharp.Core.CompiledName("AsyncGetContext")>]
  let asyncGetContext(listener:HttpListener) =
    Async.FromBeginEnd(listener.BeginGetContext, listener.EndGetContext)
    
  type System.Net.HttpListener with
    member listener.AsyncGetContext() = asyncGetContext(listener)

    static member Start(url, app, ?cancellationToken) =
      let listen (listener:HttpListener) = async {
        let! ctx = listener.AsyncGetContext()
        let context = ctx.ToContextBase()
        let request = context.ToOwinRequest()
        let! response = app request
        do context.Response.Reply(response)
        // For HttpListener, we need to close the output stream.
        ctx.Response.Close() }
      let server = async {
        use listener = new HttpListener()
        listener.Prefixes.Add(url)
        listener.Start()
        while true do
          Async.Start(listen listener, ?cancellationToken = cancellationToken) }
      Async.Start(server, ?cancellationToken = cancellationToken)     

    static member Start(url, app, ?cancellationToken) =
      HttpListener.Start(url, app |> Owin.ToAsync, ?cancellationToken = cancellationToken)
