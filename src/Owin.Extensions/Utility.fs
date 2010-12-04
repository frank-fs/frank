#nowarn "77"
namespace Owin.Extensions
open System
open System.Collections.Generic

[<AutoOpen>]
module Utility =
  let isNotNullOrEmpty s = not (String.IsNullOrEmpty(s))

  /// Dynamic indexer lookups.
  /// <see href="http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/05/using-and-abusing-the-f-dynamic-lookup-operator.aspx" />
  let inline (?) this key = ( ^a : (member get_Item : ^b -> ^c) (this,key))
  let inline (?<-) this key value = ( ^a : (member set_Item : ^b * ^c -> ^d) (this,key,value))

  /// Generic duck-typing operator.
  /// <see href="http://weblogs.asp.net/podwysocki/archive/2009/06/11/f-duck-typing-and-structural-typing.aspx" />
  let inline implicit arg = ( ^a : (static member op_Implicit : ^b -> ^a) arg)

  /// Decodes url encoded values.
  let decodeUrl input = System.Uri.UnescapeDataString(input).Replace("+", " ")

  /// Splits a relative Uri string into the path and query string.
  let splitUri (uri) =
    if System.String.IsNullOrEmpty(uri)
      then ("/", "")
      else let arr = uri.Split([|'?'|])
           let path = if arr.[0] = "/" then "/" else arr.[0].TrimEnd('/')
           let queryString = if arr.Length > 1 then arr.[1] else ""
           (path, queryString)

  /// Splits a status code into the integer status code and the string status description.
  let splitStatus (status) =
    if System.String.IsNullOrEmpty(status)
      then (200, "OK")
      else let arr = status.Split([|' '|])
           let code = int arr.[0]
           let description = if arr.Length > 1 then arr.[1] else "OK"
           (code, description)

  /// Creates a tuple from the first two values returned from a string split on the specified split character.
  let private (|/) (split:char) (input:string) =
    if input |> isNotNullOrEmpty
      then let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")
      else ("","") // This should never be reached but has to be here to satisfy the return type.

  /// Parses the url encoded string into an IDictionary<string,string>.
  let private parseUrlEncodedString input =
    if input |> isNotNullOrEmpty
      then let data = decodeUrl input
           data.Split('&')
             |> Seq.filter isNotNullOrEmpty
             |> Seq.map ((|/) '=')
             |> dict
      else dict Seq.empty

  /// Parses the query string into an IDictionary<string,string>.
  let parseQueryString = parseUrlEncodedString

  /// Parses the input stream for x-http-form-urlencoded values into an IDictionary<string,string>.
  let parseFormUrlEncoded : bytestring -> IDictionary<string,string> = (ByteString.toString >> parseUrlEncodedString)
