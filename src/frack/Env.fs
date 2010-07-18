namespace Frack
open System
open System.IO
open System.Web

type Value =
  | Str of string
  | Int of int
  | Hash of System.Collections.Generic.IDictionary<string, Value>
  | Err of TextWriter
  | Inp of TextReader
  | Ver of int * int
  | Obj of obj

/// Defines a set of convenience methods for HttpContexts and NameValueCollections.
module Extensions =
  open System.Collections.Specialized
  open System.Net

  let nameValueCollectionToTupleSeq (coll:NameValueCollection) =
    seq { for key in coll.Keys do yield (key, Str (coll.[key])) }

  /// Extends System.Collections.Specialized.NameValueCollection with methods to transform it to an enumerable, map or dictionary.
  type NameValueCollection with
    member this.AsEnumerable() = nameValueCollectionToTupleSeq this
    member this.ToDictionary() = dict (nameValueCollectionToTupleSeq this)
    member this.ToMap() =
      let folder (h:Map<string,string>) (key:string) =
        Map.add key this.[key] h 
      this.AllKeys |> Array.fold (folder) Map.empty

  /// Create an HttpContextBase from an HttpContext.
  let createFromHttpContext (context:HttpContext) =
    System.Web.HttpContextWrapper(context)

  /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
  type HttpContext with
    member this.ToContextBase() = createFromHttpContext(this)

  /// Create an HttpContextBase from an HttpContextListener.
  let createFromHttpListenerContext (context:HttpListenerContext) =
    { new HttpContextBase() with
        override this.Request =
          { new HttpRequestBase() with
              override this.HttpMethod = context.Request.HttpMethod
              override this.Url = context.Request.Url
              override this.QueryString = context.Request.QueryString
              override this.Headers = context.Request.Headers
              override this.ContentType = context.Request.ContentType
              override this.ContentLength = Convert.ToInt32(context.Request.ContentLength64) 
              override this.InputStream = context.Request.InputStream } }
              
  /// Extends System.Net.HttpListenerContext with a method to transform it into a System.Web.HttpContextBase
  type HttpListenerContext with
    member this.ToContextBase() = createFromHttpListenerContext(this)

module Env =
  open System.Collections
  open System.Text
  open Extensions

  /// Returns the script name and path info from a 
  let getPathParts (path:string) =
    if String.IsNullOrEmpty(path) then raise (ArgumentNullException("path")) 
    let p = path.TrimStart('/').Split([|'/'|], 2)  
    let scriptName = if not(String.IsNullOrEmpty(p.[0])) then "/" + p.[0] else String.Empty 
    let pathInfo   = if p.Length > 1 && not(String.IsNullOrEmpty(p.[1])) then "/" + p.[1].TrimEnd('/') else String.Empty 
    (scriptName, pathInfo)
  
  /// Creates an environment variable.
  let createEnvironment (ctx:HttpContextBase) (errors:StringBuilder) =
    // Build up the primary key value store.
    seq { yield ("HTTP_METHOD", Str ctx.Request.HttpMethod)
          yield ("SCRIPT_NAME", Str (ctx.Request.Url.AbsolutePath |> getPathParts |> fst))
          yield ("PATH_INFO", Str (ctx.Request.Url.AbsolutePath |> getPathParts |> snd))
          yield ("QUERY_STRING", Str (ctx.Request.Url.Query.TrimStart('?')))
          yield ("CONTENT_TYPE", Str ctx.Request.ContentType)
          yield ("CONTENT_LENGTH", Int ctx.Request.ContentLength)
          yield ("SERVER_NAME", Str ctx.Request.Url.Host)
          yield ("SERVER_PORT", Str (ctx.Request.Url.Port.ToString()))
          yield! ctx.Request.Headers.AsEnumerable()
          yield ("url_scheme", Str ctx.Request.Url.Scheme)
          yield ("errors", Err (TextWriter.Synchronized(new StringWriter(errors))))
          yield ("input", Inp (TextReader.Synchronized(new StreamReader(ctx.Request.InputStream))))
          yield ("version", Ver (0,1) )
        } |> dict