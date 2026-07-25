namespace Frank.Datastar

open System
open System.Buffers
open Microsoft.Extensions.Primitives

module internal ServerSentEvent =
    val internal eventPrefix : byte[]
    val internal idPrefix : byte[]
    val internal retryPrefix : byte[]
    val dataPrefix : byte[]

    val inline internal writeUtf8String : str:string -> writer:IBufferWriter<byte> -> IBufferWriter<byte>

    val inline writeUtf8Literal : bytes:byte[] -> writer:IBufferWriter<byte> -> IBufferWriter<byte>

    val inline internal writeUtf8Segment : segment:StringSegment -> writer:IBufferWriter<byte> -> IBufferWriter<byte>

    val inline writeSpace : writer:IBufferWriter<byte> -> IBufferWriter<byte>

    val inline writeNewline : writer:IBufferWriter<byte> -> unit

    val inline sendEventType : eventType:byte[] -> writer:IBufferWriter<byte> -> unit

    val inline sendEventId : eventId:string -> writer:IBufferWriter<byte> -> unit

    val inline sendRetry : retry:TimeSpan -> writer:IBufferWriter<byte> -> unit

    val inline sendDataBytesLine : dataType:byte[] -> bytes:byte[] -> writer:IBufferWriter<byte> -> unit

    val inline sendDataStringSeqLine : dataType:byte[] -> strings:string seq -> writer:IBufferWriter<byte> -> unit

    val inline sendDataStringLine : dataType:byte[] -> data:string -> writer:IBufferWriter<byte> -> unit

    val inline sendDataSegmentLine : dataType:byte[] -> segment:StringSegment -> writer:IBufferWriter<byte> -> unit

module internal String =
    val newLineChars : char[]

    val inline splitLinesToSegments : text:string -> StringSegment seq
