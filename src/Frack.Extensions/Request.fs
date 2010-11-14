namespace Frack
module Request =
  open System.Collections.Generic
  open Microsoft.Http
  open Frack

  // Reconstruct the uri.
  let private parseUri (env:Environment) =
    System.Uri(
      (read env?url_scheme) + "://" +
      (read env?SERVER_NAME) +
      (match env?SERVER_PORT with Int(v) when v <> 80 -> ":" + v.ToString() | _ -> "") +
      (read env?SCRIPT_NAME) +
      (read env?PATH_INFO) +
      (match env?QUERY_STRING with Str(v) when isNotNullOrEmpty v -> "?" + v | _ -> ""))

  /// Tests a key value pair and returns Some(header, value) for true header values; otherwise None.
  let private parseHeader (KeyValue(header, value)) =
    let nonHeaders = ["HTTP_METHOD";"SCRIPT_NAME";"PATH_INFO";"QUERY_STRING";"url_scheme";"errors";"input";"version"]
    if nonHeaders |> Seq.exists ((=) header) then None else Some(header, read value)

  /// Parses the headers from the Frack.Env into Microsoft.Http.Headers.RequestHeaders.
  let private parseHeaders (env:Environment) =
    let headers = Headers.RequestHeaders()
    env :> seq<KeyValuePair<string, Value>>
      |> Seq.choose parseHeader
      |> Seq.iter headers.Add
    headers

  /// Decodes url encoded values.
  let decodeUrl input = System.Uri.UnescapeDataString(input).Replace("+", " ")

  /// Creates a tuple from the first two values returned from a string split on the specified split character.
  let private (|/) (split:char) (input:string) =
    if input |> isNotNullOrEmpty then
      let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")
    else ("","") // This should never be reached but has to be here to satisfy the return type.

  /// Parses the url encoded string into an IDictionary<string,string>.
  let private parseUrlEncodedString input =
    if input |> isNotNullOrEmpty then
      let data = decodeUrl input
      data.Split('&')
        |> Seq.filter isNotNullOrEmpty
        |> Seq.map ((|/) '=')
        |> dict
    else dict Seq.empty

  /// Parses the query string into an IDictionary<string,string>.
  let parseQueryString (query:string) = query.TrimStart('?') |> parseUrlEncodedString

  /// Parses the input stream for x-http-form-urlencoded values into an IDictionary<string,string>.
  let parseFormUrlEncoded (input:bytestring) = input |> ByteString.toString |> parseUrlEncodedString

  /// Creates an HttpRequestMessage from a Frack.Env.
  let fromFrack (env:Environment) =
    let httpMethod = read env?HTTP_METHOD
    let uri = parseUri env
    let headers = parseHeaders env
    let input = match env?input with
                | Inp(bs) -> bs |> Seq.toArray 
                | _ -> Array.init 0 byte
    let content = HttpContent.Create(input)
    new HttpRequestMessage(httpMethod, uri, headers, content)
