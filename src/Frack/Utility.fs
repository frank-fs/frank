#nowarn "77"
namespace Frack
open System
open System.Collections.Generic

[<AutoOpen>]
module Utility =
  [<System.Runtime.CompilerServices.Extension>]
  let IsNotNullOrEmpty s = not (String.IsNullOrEmpty(s))

  /// Dynamic indexer lookups.
  /// <see href="http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/05/using-and-abusing-the-f-dynamic-lookup-operator.aspx" />
  let inline (?) this key = ( ^a : (member get_Item : ^b -> ^c) (this,key))
  let inline (?<-) this key value = ( ^a : (member set_Item : ^b * ^c -> ^d) (this,key,value))

  /// Generic duck-typing operator.
  /// <see href="http://weblogs.asp.net/podwysocki/archive/2009/06/11/f-duck-typing-and-structural-typing.aspx" />
  let inline implicit arg = ( ^a : (static member op_Implicit : ^b -> ^a) arg)

  /// Decodes url encoded values.
  let DecodeUrl input = System.Uri.UnescapeDataString(input).Replace("+", " ")

  /// Splits a relative Uri string into the path and query string.
  let SplitUri uri =
    if System.String.IsNullOrEmpty(uri)
      then ("/", "")
      else let arr = uri.Split([|'?'|])
           let path = if arr.[0] = "/" then "/" else arr.[0].TrimEnd('/')
           let queryString = if arr.Length > 1 then arr.[1] else ""
           (path, queryString)

  /// Splits a status code into the integer status code and the string status description.
  let SplitStatus status =
    if System.String.IsNullOrEmpty(status)
      then (200, "OK")
      else let arr = status.Split([|' '|])
           let code = int arr.[0]
           let description = if arr.Length > 1 then arr.[1] else "OK"
           (code, description)

  /// Creates a tuple from the first two values returned from a string split on the specified split character.
  let private (|/) (split:char) (input:string) =
    if input |> IsNotNullOrEmpty
      then let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")
      else ("","") // This should never be reached but has to be here to satisfy the return type.

  /// Parses the url encoded string into an IDictionary<string,string>.
  let private parseUrlEncodedString input =
    if input |> IsNotNullOrEmpty
      then let data = DecodeUrl input
           data.Split('&')
             |> Seq.filter IsNotNullOrEmpty
             |> Seq.map ((|/) '=')
             |> dict
      else dict Seq.empty

  /// Parses the query string into an IDictionary<string,string>.
  let ParseQueryString = parseUrlEncodedString

  /// Parses the input stream for x-http-form-urlencoded values into an IDictionary<string,string>.
  let ParseFormUrlEncoded : seq<byte> -> IDictionary<string,string> = (ByteString.toString >> parseUrlEncodedString)

  /// An active pattern for parsing the type of object returned within the response body.
  let (|Bytes|Enum|File|Segment|Str|) (item:obj) =
    match item with
    | :? seq<obj>           -> Enum(item :?> seq<obj>)
    | :? System.IO.FileInfo -> File(item :?> System.IO.FileInfo) 
    | :? ArraySegment<byte> -> Segment(item :?> ArraySegment<byte>)
    | :? string             -> Str(item :?> string)
    | _                     -> Bytes(item :?> byte[])
