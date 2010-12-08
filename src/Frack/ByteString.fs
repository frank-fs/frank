namespace Frack
open System
open System.Collections.Generic
open System.IO

/// Extensions to the Array module.
module Array =
  /// Slices out a portion of the array from the start index up to the stop index.
  let slice start stop (source:'a[]) =
    let stop' = ref stop
    if !stop' < 0 then stop' := source.Length + !stop'
    let len = !stop' - start
    [| for i in [0..(len-1)] do yield source.[i + start] |]

  [<System.Runtime.CompilerServices.Extension>]
  let Slice(arr, start, stop) = slice start stop arr

/// Extensions to dictionaries.
module Dict =
  open System.Collections.Generic
  let toSeq d = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
  let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
  let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList

/// Module to transform a string into an immutable list of bytes and back.
/// <remarks>Several extensions derived from Bent Rasumssen's Extensia project.</remarks>
/// <see href="http://extensia.codeplex.com"/>
[<AutoOpen>]
module ByteString =
  open System.Runtime.Serialization
  open System.Runtime.Serialization.Formatters.Binary
  open System.Text

  /// An empty byte string.
  let empty = Seq.empty<byte>

  /// Converts a byte string into a string.
  let toString (bs:#seq<byte>) = Encoding.UTF8.GetString(bs |> Seq.toArray)

  /// Converts a string into a byte string.
  let fromString (s: string) = Encoding.UTF8.GetBytes(s) |> Array.toSeq

  /// Converts a stream into a byte string.
  let fromStream (bufferSize:int) (stream:Stream) =
    if stream = null then raise (ArgumentNullException("stream"))
    if bufferSize <= 0 then raise (ArgumentException("bufferSize must be greater than 0.", "bufferSize"))

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
    if stream = null then raise (ArgumentNullException("stream"))
    if bufferSize <= 0 then raise (ArgumentException("bufferSize must be greater than 0.", "bufferSize"))

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
  let fromFileInfo (file:FileInfo) =
    if file = null then raise (ArgumentNullException("file"))
    use stream = file.OpenRead()
    seq { for x in fromStream 1024 stream do yield x }

  /// Converts an object to a byte seq.
  let fromObject (o:obj) =
    let formatter = BinaryFormatter()
    use stream = new MemoryStream()
    try
      formatter.Serialize(stream, o)
      stream |> fromStream 1024
    with :? SerializationException as e -> null

  /// Converts a byte string into a stream.
  let toStream source =
    if source = null then raise (ArgumentNullException("source"))
    new EnumerableStream(source) :> Stream

  /// Converts a byte string into an object.
  let toObj (source:#seq<byte>) =
    let formatter = BinaryFormatter()
    use stream = new MemoryStream(source |> Seq.toArray)
    try formatter.Deserialize(stream)
    with e -> null

  /// Transfers the bytes of a byte string into a stream
  let transfer (stream:Stream) (source:#seq<byte>) =
    if source = null then raise (ArgumentNullException("source"))
    if stream = null then raise (ArgumentNullException("stream"))
    source |> Seq.iter (fun x -> stream.WriteByte(x))

  /// Extensions to stream.
  type Stream with
    member this.ToByteString() = fromStream 1024 this
    member this.ToChunkedByteString() = fromStreamChunked 1024 this
