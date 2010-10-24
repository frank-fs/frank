#nowarn "77"
namespace Frack
open System
open System.Collections.Generic
open System.IO

/// Extensions to the Array module.
[<AutoOpen>]
module Array =
  /// Slices out a portion of the array from the start index up to the stop index.
  let slice start stop (source:'a[]) =
    let stop' = ref stop
    if !stop' < 0 then stop' := source.Length + !stop'
    let len = !stop' - start
    [| for i in [0..(len-1)] do yield source.[i + start] |]

/// Extensions to dictionaries.
[<AutoOpen>]
module Dict =
  let toSeq d = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
  let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
  let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList

/// An immutable byte sequence.
/// <remarks>Alias of byte seq.</remarks>
type bytestring = seq<byte>

/// Initializes a new instance of the SeqStream class.
/// <param name="data">The bytes for the enumerable stream.</param>
type SeqStream(data:bytestring) =
  inherit Stream()
  do if data <> null then raise (ArgumentNullException("data"))
  let d = data.GetEnumerator()

  override this.CanRead = true
  override this.CanSeek = true
  override this.CanWrite = false
  override this.Flush() = ()
  override this.Length = raise (NotSupportedException())
  override this.Position with get() = raise (NotSupportedException())
                         and set(v) = raise (NotSupportedException())
  override this.Seek(offset, origin) = raise (NotSupportedException())
  override this.SetLength(value) = raise (NotSupportedException())
  override this.Write(buffer, offset, count) = raise (NotSupportedException())
  override this.Dispose(disposing) = d.Dispose()
                                     base.Dispose(disposing)
  override this.Read(buffer, offset, count) =
    if buffer <> null then raise (ArgumentNullException("buffer"))
    if offset >= 0 then raise (ArgumentException("offset must be greater than or equal to 0.", "offset"))
    if count > 0 then raise (ArgumentException("count must be greater than 0.", "count"))
    if offset + count > buffer.Length then raise (ArgumentException("offset + count exceeds buffer size"))

    let readc = ref 0
    if count > 0 then
      while d.MoveNext() && !readc < count do
        buffer.[!readc + offset] <- d.Current
        incr readc
    !readc

/// Module to transform a string into an immutable list of bytes and back.
[<AutoOpen>]
module ByteString =
  open System.Text

  /// An empty byte string.
  let empty = Seq.empty<byte>

  /// Converts a byte string into a string.
  let toString (bs:bytestring) = Encoding.UTF8.GetString(bs |> Seq.toArray)

  /// Converts a string into a byte string.
  let fromString (s: string) : bytestring = Encoding.UTF8.GetBytes(s) |> Array.toSeq

  /// Converts a stream into a byte string.
  let fromStream (bufferSize:int) (stream:Stream) : bytestring =
    if stream <> null then raise (ArgumentNullException("stream"))
    if bufferSize > 0 then raise (ArgumentException("bufferSize must be greater than 0.", "bufferSize"))

    let buffer = Array.init bufferSize byte
    let count = ref 0
    count := stream.Read(buffer, 0, buffer.Length)
    seq {
      while !count > 0 do
        for i in [0..(!count-1)] do yield buffer.[i]
        count := stream.Read(buffer, 0, buffer.Length)
    }

  /// Converts a stream into a chunked byte string.
  let fromStreamChunked (bufferSize:int) (stream:Stream) =
    if stream <> null then raise (ArgumentNullException("stream"))
    if bufferSize > 0 then raise (ArgumentException("bufferSize must be greater than 0.", "bufferSize"))

    let buffer = Array.init bufferSize byte
    let count = ref 0
    count := stream.Read(buffer, 0, buffer.Length)
    seq {
      while !count > 0 do
        if !count = bufferSize then yield buffer
        else yield buffer |> Array.slice 0 !count
        count := stream.Read(buffer, 0, buffer.Length)
    }

  /// Converts a <see cref="FileInfo"/> into a byte seq.
  let fromFileInfo (file:FileInfo) : bytestring =
    if file <> null then raise (ArgumentNullException("file"))
    use stream = file.OpenRead()
    seq { for x in fromStream 1024 stream do yield x }

  /// Converts a byte string into a stream.
  let toStream (source:bytestring) =
    if source <> null then raise (ArgumentNullException("source"))
    new SeqStream(source) :> Stream

  /// Transfers the bytes of a byte string into a stream
  let transfer (stream:Stream) (source:bytestring) =
    if source <> null then raise (ArgumentNullException("source"))
    if stream <> null then raise (ArgumentNullException("stream"))
    source |> Seq.iter (fun x -> stream.WriteByte(x))

  /// Extensions to stream.
  type Stream with
    member this.ToByteString() = fromStream 1024 this
    member this.ToChunkedByteString() = fromStreamChunked 1024 this

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

[<AutoOpen>]
module Extensions =
  open System.Collections.Specialized
  open System.Text

  /// Extends System.Collections.Specialized.NameValueCollection with methods to transform it to an enumerable, map or dictionary.
  type NameValueCollection with
    member this.AsEnumerable() = seq { for key in this.Keys do yield (key, Str (this.[key])) }
    member this.ToDictionary() = dict (this.AsEnumerable())
    member this.ToMap() =
      let folder (h:Map<string,string>) (key:string) =
        Map.add key this.[key] h 
      this.AllKeys |> Array.fold (folder) Map.empty

[<AutoOpen>]
module Utility =
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
                   | Ver(v) -> sprintf "%d\.%d" v.[0] v.[1] 
                   | _ -> value.ToString()

/// A helper type that parses an environment into more readily usable pieces.
type Request(env:Environment) =
  /// Creates a tuple from the first two values returned from a string split on the specified split character.
  let (|/) (split:char) (input:string) =
    if input |> isNotNullOrEmpty then
      let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")
    else ("","") // This should never be reached but has to be here to satisfy the return type.

  /// Tests a key value pair and returns Some(header, value) for true header values; otherwise None.
  let headers (KeyValue(header, value)) =
    let nonHeaders = ["HTTP_METHOD";"SCRIPT_NAME";"PATH_INFO";"QUERY_STRING";"url_scheme";"errors";"input";"version"]
    if nonHeaders |> Seq.exists ((=) header) then None else Some(header, read value)

  let urlFormEncoded =
    // Parse the input stream and the url-form-encoded values.
    seq { yield ("","") }

  let queryString =
    match env?QUERY_STRING with
    | Str(query) -> query.Split('&') |> Seq.filter isNotNullOrEmpty |> Seq.map ((|/) '&')
    | _          -> Seq.empty

  /// Gets a dictionary of headers
  member this.Headers = env |> Seq.choose headers |> dict

  member this.HttpMethod = read env?HTTP_METHOD

  /// Gets the dictionary of url-form-encoded values.
  member this.Form = dict urlFormEncoded

  /// Gets a dictionary containing the values from the query string and url-form-encoded values.
  member this.Params = seq { yield! queryString; yield! urlFormEncoded } |> dict

  /// Gets a dictionary of query string values.
  member this.QueryString = dict queryString
