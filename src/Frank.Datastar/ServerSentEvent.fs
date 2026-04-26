namespace Frank.Datastar

open System
open System.Buffers
open System.Text
open Microsoft.Extensions.Primitives

module internal ServerSentEvent =
    let private eventPrefix = "event: "B
    let private idPrefix = "id: "B
    let private retryPrefix = "retry: "B
    let dataPrefix = "data: "B

    let inline private writeUtf8String (str: string) (writer: IBufferWriter<byte>) =
        let span = writer.GetSpan(Encoding.UTF8.GetByteCount(str))
        let bytesWritten = Encoding.UTF8.GetBytes(str.AsSpan(), span)
        writer.Advance(bytesWritten)
        writer

    let inline writeUtf8Literal (bytes: byte[]) (writer: IBufferWriter<byte>) =
        let span = writer.GetSpan(bytes.Length)
        bytes.AsSpan().CopyTo(span)
        writer.Advance(bytes.Length)
        writer

    let inline private writeUtf8Segment (segment: StringSegment) (writer: IBufferWriter<byte>) =
        let span = writer.GetSpan(Encoding.UTF8.GetByteCount(segment))
        let bytesWritten = Encoding.UTF8.GetBytes(segment.AsSpan(), span)
        writer.Advance(bytesWritten)
        writer

    let inline writeSpace (writer: IBufferWriter<byte>) =
        let span = writer.GetSpan(1)
        span[0] <- 32uy // ' '
        writer.Advance(1)
        writer

    let inline writeNewline (writer: IBufferWriter<byte>) =
        let span = writer.GetSpan(1)
        span[0] <- 10uy // '\n'
        writer.Advance(1)

    let inline sendEventType eventType writer =
        writer
        |> writeUtf8Literal eventPrefix
        |> writeUtf8Literal eventType
        |> writeNewline

    let inline sendEventId eventId writer =
        writer |> writeUtf8Literal idPrefix |> writeUtf8String eventId |> writeNewline

    let inline sendRetry (retry: TimeSpan) writer =
        writer
        |> writeUtf8Literal retryPrefix
        |> writeUtf8String (retry.TotalMilliseconds.ToString())
        |> writeNewline

    let inline sendDataBytesLine dataType bytes writer =
        writer
        |> writeUtf8Literal dataPrefix
        |> writeUtf8Literal dataType
        |> writeSpace
        |> writeUtf8Literal bytes
        |> writeNewline

    let inline sendDataStringSeqLine dataType strings writer =
        writer
        |> writeUtf8Literal dataPrefix
        |> writeUtf8Literal dataType
        |> writeSpace
        |> (fun writer ->
            strings
            |> Seq.iter (fun string -> writer |> writeUtf8String string |> writeSpace |> ignore)

            writer)
        |> writeNewline

    let inline sendDataStringLine dataType data writer =
        writer
        |> writeUtf8Literal dataPrefix
        |> writeUtf8Literal dataType
        |> writeSpace
        |> writeUtf8String data
        |> writeNewline

    let inline sendDataSegmentLine dataType segment writer =
        writer
        |> writeUtf8Literal dataPrefix
        |> writeUtf8Literal dataType
        |> writeSpace
        |> writeUtf8Segment segment
        |> writeNewline

module internal String =
    let newLineChars = [| '\r'; '\n' |]

    // Zero-allocation version using StringTokenizer
    let inline splitLinesToSegments (text: string) =
        StringTokenizer(text, newLineChars)
        |> Seq.filter (fun segment -> segment.Length > 0)
