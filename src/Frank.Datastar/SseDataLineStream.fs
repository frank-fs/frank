namespace Frank.Datastar

open System
open System.Buffers
open System.IO
open System.Threading

/// Stream subclass that bridges caller byte output to SSE-formatted data lines on IBufferWriter<byte>.
/// Unlike SseDataLineWriter (char-based), this operates entirely in byte-land — no encoding needed.
/// In UTF-8, 0x0A is always a newline (never part of a multi-byte sequence), so byte scanning is safe.
type internal SseDataLineStream(bufferWriter: IBufferWriter<byte>, dataLineType: byte[], cancellationToken: CancellationToken) =
    inherit Stream()

    let mutable byteBuffer = ArrayPool<byte>.Shared.Rent(256)
    let mutable position = 0
    let mutable disposed = false

    let ensureCapacity () =
        let newBuffer = ArrayPool<byte>.Shared.Rent(byteBuffer.Length * 2)
        byteBuffer.AsSpan(0, position).CopyTo(newBuffer.AsSpan())
        ArrayPool<byte>.Shared.Return(byteBuffer)
        byteBuffer <- newBuffer

    let emitLine () =
        if position > 0 then
            cancellationToken.ThrowIfCancellationRequested()
            bufferWriter |> ServerSentEvent.writeUtf8Literal ServerSentEvent.dataPrefix |> ignore
            bufferWriter |> ServerSentEvent.writeUtf8Literal dataLineType |> ignore
            bufferWriter |> ServerSentEvent.writeSpace |> ignore
            let span = bufferWriter.GetSpan(position)
            byteBuffer.AsSpan(0, position).CopyTo(span)
            bufferWriter.Advance(position)
            bufferWriter |> ServerSentEvent.writeNewline
            position <- 0

    override _.CanRead = false
    override _.CanSeek = false
    override _.CanWrite = true
    override _.Length = raise (NotSupportedException())
    override _.Position with get() = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
    override _.Read(_, _, _) = raise (NotSupportedException())
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength(_) = raise (NotSupportedException())

    override _.Write(buffer: byte array, offset: int, count: int) =
        let mutable i = offset
        let endIdx = offset + count
        while i < endIdx do
            let b = buffer[i]
            match b with
            | 0x0Auy -> // \n
                emitLine ()
            | 0x0Duy -> // \r
                emitLine ()
                if i + 1 < endIdx && buffer[i + 1] = 0x0Auy then
                    i <- i + 1
            | _ ->
                if position >= byteBuffer.Length then ensureCapacity ()
                byteBuffer[position] <- b
                position <- position + 1
            i <- i + 1

    override _.Write(buffer: ReadOnlySpan<byte>) =
        let mutable i = 0
        while i < buffer.Length do
            let b = buffer[i]
            match b with
            | 0x0Auy ->
                emitLine ()
            | 0x0Duy ->
                emitLine ()
                if i + 1 < buffer.Length && buffer[i + 1] = 0x0Auy then
                    i <- i + 1
            | _ ->
                if position >= byteBuffer.Length then ensureCapacity ()
                byteBuffer[position] <- b
                position <- position + 1
            i <- i + 1

    override _.Flush() =
        emitLine ()

    override _.Dispose(disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                emitLine ()
                ArrayPool<byte>.Shared.Return(byteBuffer)
        base.Dispose(disposing)
