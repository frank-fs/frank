namespace Frank.Types

/// Module to transform a string into an immutable list of bytes and back.
[<AutoOpen>]
module ByteString =
  /// Converts a byte string into a string.
  let toString bs = System.Text.Encoding.UTF8.GetString(bs |> Seq.toArray)
  /// Converts a string into a byte string.
  let toByteString (s: string) = System.Text.Encoding.UTF8.GetBytes(s) |> Array.toSeq
  type System.String with
    /// Converts a string into a byte string.
    member this.ToByteString() = toByteString(this)