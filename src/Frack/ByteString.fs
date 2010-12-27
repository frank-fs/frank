namespace Frack

/// Module to transform a string into an immutable list of bytes and back.
/// See http://extensia.codeplex.com/
[<AutoOpen>]
module ByteString =
  open System
  open System.Collections.Generic
  open System.Diagnostics.Contracts
  open System.IO
  open System.Runtime.Serialization
  open System.Runtime.Serialization.Formatters.Binary
  open System.Text

  type bytestring = seq<byte>

  /// An empty byte string.
  let empty = Seq.empty<byte>

  /// Converts a byte string into a string.
  let toString bs = Encoding.UTF8.GetString(bs |> Seq.toArray)

  /// Converts a string into a byte string.
  let fromString (s:string) = Encoding.UTF8.GetBytes(s) |> Array.toSeq

  /// Converts a stream into a byte string.
  let fromStream bufferSize (stream:Stream) =
    Contract.Requires(stream <> null)
    Contract.Requires(bufferSize > 0)

    let buffer = Array.init bufferSize byte
    let count = ref 0
    count := stream.Read(buffer, 0, buffer.Length)
    seq { while !count > 0 do
            for i in [0..(!count-1)] do yield buffer.[i]
            count := stream.Read(buffer, 0, buffer.Length) }

  /// Converts a stream into a chunked byte string.
  let fromStreamChunked bufferSize (stream:Stream) =
    Contract.Requires(stream <> null)
    Contract.Requires(bufferSize > 0)

    let buffer = Array.init bufferSize byte
    let count = ref 0
    count := stream.Read(buffer, 0, buffer.Length)
    seq { while !count > 0 do
            if !count = bufferSize then yield buffer
            else yield buffer |> Array.slice 0 !count
            count := stream.Read(buffer, 0, buffer.Length) }

  /// Converts a <see cref="FileInfo"/> into a byte seq.
  let fromFileInfo (file:FileInfo) =
    if file = null then raise (ArgumentNullException("file"))
    use stream = file.OpenRead()
    seq { for x in fromStream 1024 stream do yield x }

  /// Converts an object to a byte seq.
  let fromObject o =
    let formatter = BinaryFormatter()
    use stream = new MemoryStream()
    try
      formatter.Serialize(stream, o)
      stream |> fromStream 1024
    with :? SerializationException as e -> null

  /// Converts a byte string into a stream.
  let toStream source =
    Contract.Requires(source <> null)
    new SeqStream(source) :> Stream

  /// Converts a byte string into an object.
  let cast<'a when 'a : null> source =
    let formatter = BinaryFormatter()
    use stream = new MemoryStream(source |> Seq.toArray)
    try formatter.Deserialize(stream) :?> 'a
    with e -> null

  /// Transfers the bytes of a byte string into a stream
  let transfer (stream:Stream) source =
    Contract.Requires(source <> null)
    Contract.Requires(stream <> null)
    source |> Seq.iter (fun x -> stream.WriteByte(x))

  /// Extensions to stream.
  type Stream with
    member this.ToByteString() = fromStream 1024 this
    member this.ToChunkedByteString() = fromStreamChunked 1024 this
