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
  do if data = null then raise (ArgumentNullException("data"))
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
    if buffer = null then raise (ArgumentNullException("buffer"))
    if offset < 0 then raise (ArgumentException("offset must be greater than or equal to 0.", "offset"))
    if count <= 0 then raise (ArgumentException("count must be greater than 0.", "count"))
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
  let fromFileInfo (file:FileInfo) : bytestring =
    if file = null then raise (ArgumentNullException("file"))
    use stream = file.OpenRead()
    seq { for x in fromStream 1024 stream do yield x }

  /// Converts a byte string into a stream.
  let toStream (source:bytestring) =
    if source = null then raise (ArgumentNullException("source"))
    new SeqStream(source) :> Stream

  /// Transfers the bytes of a byte string into a stream
  let transfer (stream:Stream) (source:bytestring) =
    if source = null then raise (ArgumentNullException("source"))
    if stream = null then raise (ArgumentNullException("stream"))
    source |> Seq.iter (fun x -> stream.WriteByte(x))

  /// Extensions to stream.
  type Stream with
    member this.ToByteString() = fromStream 1024 this
    member this.ToChunkedByteString() = fromStreamChunked 1024 this