namespace Frack
module Env =
  open System
  open System.IO
  open System.Text
  open System.Web
  open Frack.Extensions
  
  /// Returns the script name and path info from a 
  let getPathParts (path:string) =
    if String.IsNullOrEmpty(path) then raise (ArgumentNullException("path")) 

    let p = path.TrimStart('/').Split([|'/'|], 2)  
    let scriptName = if not(String.IsNullOrEmpty(p.[0])) then "/" + p.[0] else String.Empty 
    let pathInfo   = if p.Length > 1 && not(String.IsNullOrEmpty(p.[1])) then "/" + p.[1].TrimEnd('/') else String.Empty 
    (scriptName, pathInfo)
  
  /// Creates an environment and errors output.
  let create (ctx:HttpContextBase) =
    let errors = StringBuilder()
    
    // Return a reference to the errors string builder as well as the environment.
    (errors,
     {HTTP_METHOD = ctx.Request.HttpMethod
      SCRIPT_NAME = ctx.Request.Url.AbsolutePath |> getPathParts |> fst
      PATH_INFO = ctx.Request.Url.AbsolutePath |> getPathParts |> snd
      QUERY_STRING = ctx.Request.Url.Query.TrimStart('?')
      CONTENT_TYPE = ctx.Request.ContentType
      CONTENT_LENGTH = ctx.Request.ContentLength
      SERVER_NAME = ctx.Request.Url.Host
      SERVER_PORT = ctx.Request.Url.Port
      HEADERS = ctx.Request.Headers.ToMap()
      QueryString = ctx.Request.QueryString.ToMap()
      Version = (0,1)
      UrlScheme = ctx.Request.Url.Scheme
      Input = TextReader.Synchronized(new StreamReader(ctx.Request.InputStream))
      Errors = TextWriter.Synchronized(new StringWriter(errors))
      Multithread = ref true
      Multiprocess = ref true
      RunOnce = ref false})