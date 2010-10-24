#nowarn "77"
namespace Frack
open System
open System.Collections.Generic

/// Defines a discriminated union of types that may be provided in the Frack Request.
type Value =
  | Str of string
  | Int of int
  | Err of bytestring
  | Inp of bytestring
  | Ver of int array

/// Defines the type for a Frack request.
type Environment = IDictionary<string, Value>

/// Defines the type for a Frack response.
type Response = int * IDictionary<string, string> * seq<bytestring>

/// Defines the type for a Frack application.
type App = delegate of Environment -> Response

/// Defines the type for a Frack middleware.
type Middleware = delegate of App -> Response

[<AutoOpen>]
module Core =
  /// Returns the script name and path info from a url.
  let getPathParts (path:string) =
    if String.IsNullOrEmpty(path) then raise (ArgumentNullException("path")) 
    let p = path.TrimStart('/').Split([|'/'|], 2)  
    let scriptName = if not(String.IsNullOrEmpty(p.[0])) then "/" + p.[0] else ""
    let pathInfo   = if p.Length > 1 && not(String.IsNullOrEmpty(p.[1])) then "/" + p.[1].TrimEnd('/') else ""
    (scriptName, pathInfo)

  /// Dynamic indexer lookups.
  /// <see href="http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/05/using-and-abusing-the-f-dynamic-lookup-operator.aspx" />
  let inline (?) this key = ( ^a : (member get_Item : ^b -> ^c) (this,key))
  let inline (?<-) this key value = ( ^a : (member set_Item : ^b * ^c -> ^d) (this,key,value))

  /// Generic duck-typing operator.
  /// <see href="http://weblogs.asp.net/podwysocki/archive/2009/06/11/f-duck-typing-and-structural-typing.aspx" />
  let inline implicit arg = ( ^a : (static member op_Implicit : ^b -> ^a) arg)

  let isNotNullOrEmpty s = not (String.IsNullOrEmpty(s))

  /// Reads a Frack.Value and returns a string result.
  let read value = match value with
                   | Str(v) -> v
                   | Int(v) -> v.ToString()
                   | Ver(v) -> sprintf "%d.%d" v.[0] v.[1] 
                   | _      -> value.ToString()