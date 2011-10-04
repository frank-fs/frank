[<AutoOpen>]
module Frank.Utility

#nowarn "77"
open System

let isNullOrEmpty = String.IsNullOrEmpty
let isNotNullOrEmpty = not << String.IsNullOrEmpty

/// Dynamic indexer lookups.
/// <see href="http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/05/using-and-abusing-the-f-dynamic-lookup-operator.aspx" />
let inline (?) this key = ( ^a : (member get_Item : ^b -> ^c) (this,key))
let inline (?<-) this key value = ( ^a : (member set_Item : ^b * ^c -> ^d) (this,key,value))

/// Generic duck-typing operator.
/// <see href="http://weblogs.asp.net/podwysocki/archive/2009/06/11/f-duck-typing-and-structural-typing.aspx" />
let inline implicit arg = ( ^a : (static member op_Implicit : ^b -> ^a) arg)

/// Decodes url encoded values.
let decodeUrl input = Uri.UnescapeDataString(input)

/// Splits a relative Uri string into the path and query string.
let splitUri uri =
  if uri |> isNullOrEmpty then ("/", "")
  else let arr = uri.Split([|'?'|])
       let path = if arr.[0] = "/" then "/" else arr.[0].TrimEnd('/')
       let queryString = if arr.Length > 1 then arr.[1] else ""
       (path, queryString)

/// Splits a status code into the integer status code and the string status description.
let splitStatus status =
  if status |> isNullOrEmpty then (200, "OK")
  else let arr = status.Split([|' '|])
       let code = int arr.[0]
       let description = if arr.Length > 1 then arr.[1] else "OK"
       (code, description)

/// Creates a tuple from the first two values returned from a string split on the specified split character.
let private (|/) (split:char) (input:string) =
  if input |> isNullOrEmpty then ("","") // Should never occur.
  else let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")

/// Parses the url encoded string into a seq<string * string>
let private parseUrlEncodedString input =
  if input |> isNullOrEmpty then Dict.empty
  else let data = decodeUrl input
       data.Split('&')
       |> Seq.filter isNotNullOrEmpty
       |> Seq.map ((|/) '=')
       |> dict
  
/// Parses the query string into a seq<string * string>.
let parseQuery = parseUrlEncodedString
 
/// Parses the input stream for x-http-form-urlencoded values into a seq<string * string>.
let parseForm = System.Text.Encoding.UTF8.GetString >> parseUrlEncodedString
