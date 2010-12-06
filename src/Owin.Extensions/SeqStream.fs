namespace Owin
open System
open System.IO

/// Initializes a new instance of the SeqStream class.
/// <param name="data">The bytes for the enumerable stream.</param>
/// <remarks>The implementation is derived from Bent Rasumssen's Extensia project.</remarks>
/// <see href="http://extensia.codeplex.com"/>
type SeqStream(data:seq<byte>) =
  inherit Stream()
  do if data = null then raise (ArgumentNullException("data"))
  let d = data.GetEnumerator()

  override this.CanRead = true
  override this.CanSeek = true
  override this.CanWrite = false
  override this.Flush() = ()
  override this.Length = data |> Seq.length |> int64
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