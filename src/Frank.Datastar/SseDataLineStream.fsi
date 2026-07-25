namespace Frank.Datastar

open System
open System.Buffers
open System.IO
open System.Threading

/// Stream subclass that bridges caller byte output to SSE-formatted data lines on IBufferWriter<byte>.
/// Unlike SseDataLineWriter (char-based), this operates entirely in byte-land — no encoding needed.
/// In UTF-8, 0x0A is always a newline (never part of a multi-byte sequence), so byte scanning is safe.
type internal SseDataLineStream =
    inherit Stream
    new : bufferWriter:IBufferWriter<byte> * dataLineType:byte[] * cancellationToken:CancellationToken -> SseDataLineStream

    override CanRead : bool
    override CanSeek : bool
    override CanWrite : bool
    override Length : int64
    override Position : int64 with get, set
    override Read : buffer:byte[] * offset:int * count:int -> int
    override Seek : offset:int64 * origin:SeekOrigin -> int64
    override SetLength : value:int64 -> unit
    override Write : buffer:byte[] * offset:int * count:int -> unit
    override Write : buffer:ReadOnlySpan<byte> -> unit
    override Flush : unit -> unit
