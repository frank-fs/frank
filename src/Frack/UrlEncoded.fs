namespace Frack
module UrlEncoded =
  open Frack.Collections

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
  let parseForm data = data |> ByteString.toString |> parseUrlEncodedString

