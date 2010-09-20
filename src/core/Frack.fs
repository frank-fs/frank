#nowarn "77"
namespace Frack
open System
open System.IO

type Value =
  | Str of string
  | Int of int
  | Hash of System.Collections.Generic.IDictionary<string, Value>
  | Err of TextWriter
  | Inp of TextReader
  | Ver of int array
  | Obj of obj


[<AutoOpen>]
module Env =
  /// Returns the script name and path info from a 
  let getPathParts (path:string) =
    if String.IsNullOrEmpty(path) then raise (ArgumentNullException("path")) 
    let p = path.TrimStart('/').Split([|'/'|], 2)  
    let scriptName = if not(String.IsNullOrEmpty(p.[0])) then "/" + p.[0] else String.Empty 
    let pathInfo   = if p.Length > 1 && not(String.IsNullOrEmpty(p.[1])) then "/" + p.[1].TrimEnd('/') else String.Empty 
    (scriptName, pathInfo)
  

[<AutoOpen>]
module Extensions =
  open System.Collections.Specialized
  open System.Text

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

  /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
  type System.Web.HttpContext with
    member this.ToContextBase() = System.Web.HttpContextWrapper(this)

  type System.Web.HttpContextBase with
    /// Creates an environment variable <see cref="HttpContextBase"/>.
    member this.ToFrackEnv(errors:StringBuilder) =
      seq { yield ("HTTP_METHOD", Str this.Request.HttpMethod)
            yield ("SCRIPT_NAME", Str (this.Request.Url.AbsolutePath |> getPathParts |> fst))
            yield ("PATH_INFO", Str (this.Request.Url.AbsolutePath |> getPathParts |> snd))
            yield ("QUERY_STRING", Str (this.Request.Url.Query.TrimStart('?')))
            yield ("CONTENT_TYPE", Str this.Request.ContentType)
            yield ("CONTENT_LENGTH", Int this.Request.ContentLength)
            yield ("SERVER_NAME", Str this.Request.Url.Host)
            yield ("SERVER_PORT", Str (this.Request.Url.Port.ToString()))
            yield! this.Request.Headers.AsEnumerable()
            yield ("url_scheme", Str this.Request.Url.Scheme)
            yield ("errors", Err (TextWriter.Synchronized(new StringWriter(errors))))
            yield ("input", Inp (TextReader.Synchronized(new StreamReader(this.Request.InputStream))))
            yield ("version", Ver [|0;1|] )
          } |> dict

  type System.Net.HttpListenerContext with
    /// Creates an environment variable from an <see cref="HttpListenerContext"/>.
    member this.ToFrackEnv(errors:StringBuilder) =
      seq { yield ("HTTP_METHOD", Str this.Request.HttpMethod)
            yield ("SCRIPT_NAME", Str (this.Request.Url.AbsolutePath |> getPathParts |> fst))
            yield ("PATH_INFO", Str (this.Request.Url.AbsolutePath |> getPathParts |> snd))
            yield ("QUERY_STRING", Str (this.Request.Url.Query.TrimStart('?')))
            yield ("CONTENT_TYPE", Str this.Request.ContentType)
            yield ("CONTENT_LENGTH", Int (Convert.ToInt32(this.Request.ContentLength64))) 
            yield ("SERVER_NAME", Str this.Request.Url.Host)
            yield ("SERVER_PORT", Str (this.Request.Url.Port.ToString()))
            yield! this.Request.Headers.AsEnumerable()
            yield ("url_scheme", Str this.Request.Url.Scheme)
            yield ("errors", Err (TextWriter.Synchronized(new StringWriter(errors))))
            yield ("input", Inp (TextReader.Synchronized(new StreamReader(this.Request.InputStream))))
            yield ("version", Ver [|0;1|] )
          } |> dict
    

[<AutoOpen>]
module Utility =
  /// Dynamic indexer lookups.
  /// <see href="http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/05/using-and-abusing-the-f-dynamic-lookup-operator.aspx" />
  let inline (?) this key =
    ( ^a : (member get_Item : ^b -> ^c) (this,key))
  let inline (?<-) this key value =
    ( ^a : (member set_Item : ^b * ^c -> ^d) (this,key,value))

  /// Generic duck-typing operator.
  /// <see href="http://weblogs.asp.net/podwysocki/archive/2009/06/11/f-duck-typing-and-structural-typing.aspx" />
  let inline implicit arg =
    ( ^a : (static member op_Implicit : ^b -> ^a) arg)


module Middleware =
  open System.Collections.Generic

  let printEnvironment app =
    fun (env:IDictionary<string,Value>) ->
      let status, hdrs, body = app env
      let vars = seq { for key in env.Keys do
                         let value = match env.[key] with
                                     | Str(v) -> v
                                     | Int(v) -> v.ToString()
                                     | Hash(v) -> v.ToString()
                                     | Err(v) -> v.ToString()
                                     | Inp(v) -> v.ToString()
                                     | Ver(v) -> v.[0].ToString() + "." + v.[1].ToString()
                                     | Obj(v) -> v.ToString()
                         yield key + ": " + value }
      let bd = seq { yield! body; yield! vars }
               |> Seq.filter (fun v -> not(String.IsNullOrEmpty(v)))
      ( status, hdrs, bd )
