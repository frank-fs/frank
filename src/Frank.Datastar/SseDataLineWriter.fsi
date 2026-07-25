namespace Frank.Datastar

open System.Buffers
open System.IO
open System.Text
open System.Threading

type internal SseDataLineWriter =
    inherit TextWriter
    new : bufferWriter:IBufferWriter<byte> * dataLineType:byte[] * cancellationToken:CancellationToken -> SseDataLineWriter

    override Encoding : Encoding
    override Write : value:char -> unit
    override Write : value:string -> unit
    override Flush : unit -> unit
