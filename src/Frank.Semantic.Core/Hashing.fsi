namespace Frank.Semantic

/// Single implementation of SHA-256 hex encoding shared by LockFile and VocabFetcher.
/// Produces a 64-character lowercase hex string with no separators.
module Hashing =

    /// SHA-256 hex string (lowercase, 64 chars) of the given byte array.
    val sha256Hex: bytes: byte[] -> string
