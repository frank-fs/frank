namespace Frank.Types

/// Module to transform <see cref="System.String" /> into an immutable list of bytes and back.
[<AutoOpen>]
module ByteString =
  /// Converts a byte string into a <see cref="System.String" />.
  let toString bs = System.Text.Encoding.UTF8.GetString(bs |> Seq.toArray)
  /// Converts a <see cref="System.String" /> into a byte string.
  let toByteString (s: string) = System.Text.Encoding.UTF8.GetBytes(s) |> Array.toSeq
  type System.String with
    /// Converts a <see cref="System.String" /> into a byte string.
    member this.ToByteString() = toByteString(this)