namespace Frank.Datastar

open System
open System.Buffers
open System.IO
open System.Text
open System.Threading

type internal SseDataLineWriter(bufferWriter: IBufferWriter<byte>, dataLineType: byte[], cancellationToken: CancellationToken) =
    inherit TextWriter()

    let mutable charBuffer = ArrayPool<char>.Shared.Rent(256)
    let mutable position = 0
    let mutable disposed = false

    let ensureCapacity () =
        let newBuffer = ArrayPool<char>.Shared.Rent(charBuffer.Length * 2)
        charBuffer.AsSpan(0, position).CopyTo(newBuffer.AsSpan())
        ArrayPool<char>.Shared.Return(charBuffer)
        charBuffer <- newBuffer

    let emitLine () =
        if position > 0 then
            cancellationToken.ThrowIfCancellationRequested()
            bufferWriter |> ServerSentEvent.writeUtf8Literal ServerSentEvent.dataPrefix |> ignore
            bufferWriter |> ServerSentEvent.writeUtf8Literal dataLineType |> ignore
            bufferWriter |> ServerSentEvent.writeSpace |> ignore
            let charSpan = ReadOnlySpan(charBuffer, 0, position)
            let byteCount = Encoding.UTF8.GetByteCount(charSpan)
            let byteSpan = bufferWriter.GetSpan(byteCount)
            let bytesWritten = Encoding.UTF8.GetBytes(charSpan, byteSpan)
            bufferWriter.Advance(bytesWritten)
            bufferWriter |> ServerSentEvent.writeNewline
            position <- 0

    override _.Encoding = Encoding.UTF8

    override _.Write(value: char) =
        match value with
        | '\n' -> emitLine ()
        | '\r' -> () // skip \r — will be followed by \n or treated as line boundary
        | _ ->
            if position >= charBuffer.Length then ensureCapacity ()
            charBuffer[position] <- value
            position <- position + 1

    override _.Write(value: string) =
        if not (isNull value) then
            let mutable i = 0
            while i < value.Length do
                let ch = value[i]
                match ch with
                | '\r' ->
                    emitLine ()
                    // skip \n following \r
                    if i + 1 < value.Length && value[i + 1] = '\n' then
                        i <- i + 1
                | '\n' ->
                    emitLine ()
                | _ ->
                    if position >= charBuffer.Length then ensureCapacity ()
                    charBuffer[position] <- ch
                    position <- position + 1
                i <- i + 1

    override _.Flush() =
        emitLine ()

    override _.Dispose(disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                emitLine ()
                ArrayPool<char>.Shared.Return(charBuffer)
        base.Dispose(disposing)
