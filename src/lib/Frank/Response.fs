module Frank.Response

open System
open System.IO
open System.Text
open Stream

let rec getBytes item =
  match item with
  | Sequence it -> seq { for i in it do yield! getBytes i } |> Seq.toArray
  | Bytes bs -> bs
  | Str str -> str |> Encoding.UTF8.GetBytes
  | Segment seg ->
      let dst = Array.zeroCreate<byte> seg.Count
      Buffer.BlockCopy(seg.Array, seg.Offset, dst, 0, seg.Count)
      dst

let write (stream: System.IO.Stream) item =
  let bytes = getBytes item
  stream.Write(bytes, 0, bytes.Length)
