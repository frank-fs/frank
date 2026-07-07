namespace Frank.Semantic

open System.Security.Cryptography

/// Single implementation of SHA-256 hex encoding shared by LockFile and VocabFetcher.
/// Produces a 64-character lowercase hex string with no separators.
module Hashing =

    /// SHA-256 hex string (lowercase, 64 chars) of the given byte array.
    let sha256Hex (bytes: byte[]) : string =
        use sha = SHA256.Create()
        let hash = sha.ComputeHash bytes
        System.Convert.ToHexString(hash).ToLowerInvariant()
