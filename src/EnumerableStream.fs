namespace Frank.Types
open System.Diagnostics.Contracts
open System.IO

/// Initializes a new instance of the <see cref="EnumerableStream"/> class.
/// <param name="data">The bytes for the enumerable stream.</param>
type EnumerableStream(data: seq<byte>) =
  inherit Stream()
  do Contract.Requires(data <> null)
  let d = data.GetEnumerator()

  override this.CanRead = true
  override this.CanSeek = true
  override this.CanWrite = false
  override this.Flush() = ()
  override this.Length = raise (System.NotSupportedException())
  override this.Position with get() = raise (System.NotSupportedException())
                         and set(v) = raise (System.NotSupportedException())
  override this.Seek(offset, origin) = raise (System.NotSupportedException())
  override this.SetLength(value) = raise (System.NotSupportedException())
  override this.Write(buffer, offset, count) = raise (System.NotSupportedException())
  override this.Dispose(disposing) = d.Dispose()
                                     base.Dispose(disposing)
  override this.Read(buffer, offset, count) =
    Contract.Requires(buffer <> null)
    Contract.Requires(offset >= 0)
    Contract.Requires(count > 0)
    if offset + count > buffer.Length then
      raise (System.ArgumentException("offset + count exceeds buffer size"))
    let readc = ref 0
    if count > 0 then
      while d.MoveNext() && !readc < count do
        buffer.[!readc + offset] <- d.Current
        incr readc
    !readc

[<AutoOpen>]
module EStream =
  /// Converts a <see cref="Stream"/> into a byte seq.
  let toSeq (bufferSize:int) (stream:Stream) =
    Contract.Requires(stream <> null)
    Contract.Requires(bufferSize > 0)
    let buffer = Array.init bufferSize byte
    let count = ref 0
    count := stream.Read(buffer, 0, buffer.Length)
    seq {
      while !count > 0 do
        for i in [0..(!count-1)] do yield buffer.[i]
        count := stream.Read(buffer, 0, buffer.Length)
    }

  /// Converts a <see cref="Stream"/> into a byte[] seq.
  let toSeqChunks (bufferSize:int) (stream:Stream) =
    Contract.Requires(stream <> null)
    Contract.Requires(bufferSize > 0)
    let buffer = Array.init bufferSize byte
    let count = ref 0
    count := stream.Read(buffer, 0, buffer.Length)
    seq {
      while !count > 0 do
        if !count = bufferSize then yield buffer
        else yield buffer |> ArrayEx.slice 0 !count
        count := stream.Read(buffer, 0, buffer.Length)
    }

  /// Extensions to <see cref="Stream"/>.
  type Stream with
    member this.ToEnumerable() = toSeq 1024 this
    member this.ToEnumerableChunks() = toSeqChunks 1024 this
    
module EFileInfo =
  /// Converts a <see cref="FileInfo"/> into a byte seq.
  let toSeq (file:FileInfo) =
    Contract.Requires(file <> null)
    use stream = file.OpenRead()
    seq { for x in stream.ToEnumerable() do yield x }

  /// Extensions to <see cref="FileInfo"/>.
  type FileInfo with
    member this.ToEnumerable() = toSeq this

[<AutoOpen>]
module ByteSeq =
  /// Converts a byte seq into a <see cref="Stream"/>.
  let toStream (source:seq<byte>) =
    Contract.Requires(source <> null)
    new EnumerableStream(source) :> Stream

  /// Transfers the bytes of a byte seq into a <see cref="Stream"/>.
  let transfer (stream:Stream) (source:seq<byte>) =
    Contract.Requires(source <> null)
    Contract.Requires(stream <> null)
    source |> Seq.iter (fun x -> stream.WriteByte(x))