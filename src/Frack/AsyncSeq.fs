namespace Frack

/// Represents a sequence of values 'T where items 
/// are generated asynchronously on-demand
/// http://fssnip.net/1k
type AsyncSeq<'a> = Async<AsyncSeqInner<'a>>
and AsyncSeqInner<'a> =
  | Ended
  | Item of 'a * AsyncSeq<'a>

module ASeq =
  open System.IO
  open System.Net.Sockets

  /// Read socket 'socket' in blocks of size 'size'
  /// (returns on-demand asynchronous sequence)
  let receiveInBlocks size (socket:Socket) = async {
    let buffer = Array.zeroCreate size

    /// Returns next block as 'Item' of async seq.
    let rec nextBlock() = async {
      let! count = socket.AsyncReceive(buffer, 0, size)
      if count = 0 then return Ended
      else
        // Create buffer with the right size
        let res =
          if count = size then buffer
          else buffer |> Seq.take count |> Array.ofSeq
        return Item(res, nextBlock()) }
        
    return! nextBlock() }

  /// Read stream 'stream' in blocks of size 'size'
  /// (returns on-demand asynchronous sequence)
  let readInBlocks size (stream:Stream) = async {
    let buffer = Array.zeroCreate size

    /// Returns next block as 'Item' of async seq.
    let rec nextBlock() = async {
      let! count = stream.AsyncRead(buffer, 0, size)
      if count = 0 then return Ended
      else
        // Create buffer with the right size
        let res =
          if count = size then buffer
          else buffer |> Seq.take count |> Array.ofSeq
        return Item(res, nextBlock()) }
        
    return! nextBlock() }

  /// Asynchronous function that greedily creates a Seq from an AsyncSeq.
  let toSeq aseq =
    let rec read cont aseq = async {
      let! item = aseq
      match item with
      | Ended -> return cont [] |> Seq.ofList
      | Item(hd, tl) -> return! read (fun rest -> hd::rest) tl}
    read id aseq

  /// Asynchronous function that creates a lazy Seq from an AsyncSeq.
  /// Be careful to manage the lifetime of the underlying provider.
  let toLazySeq aseq =
    let rec read cont aseq = async {
      let! item = aseq
      match item with
      | Ended -> return cont Seq.empty
      | Item(hd, tl) -> return! read (fun rest -> seq { yield hd; yield! rest }) tl}
    read id aseq

  /// Asynchronous function that compares two asynchronous sequences
  /// item by item. If an item doesn't match, 'false' is returned
  /// immediately without generating the rest of the sequence. If the
  /// lengths don't match, exception is thrown.
  let rec compareAsyncSeqs seq1 seq2 = async {
    let! item1 = seq1
    let! item2 = seq2
    match item1, item2 with 
    | Item(b1, ns1), Item(b2, ns2) when b1 <> b2 -> return false
    | Item(b1, ns1), Item(b2, ns2) -> return! compareAsyncSeqs ns1 ns2
    | Ended, Ended -> return true
    | _ -> return failwith "Size doesn't match" }