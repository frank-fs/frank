namespace Frank

open System
open Microsoft.Extensions.Logging

/// A cached ETag entry with access tracking for LRU eviction.
[<Struct>]
type CacheEntry =
    { ETag: string
      LastAccessed: DateTimeOffset
      ComputedAt: DateTimeOffset }

/// Statistics for the ETag cache.
[<Struct>]
type CacheStats =
    { EntryCount: int
      HitCount: int64
      MissCount: int64 }

/// A MailboxProcessor-backed concurrent cache for ETag values with LRU eviction.
type ETagCache =
    new: maxEntries: int * logger: ILogger<ETagCache> -> ETagCache

    /// Retrieves the cached ETag for the given resource key, updating its LastAccessed time.
    member GetETag: resourceKey: string -> Async<string option>

    /// Stores an ETag value, evicting the least-recently-used entry if capacity is exceeded.
    member SetETag: resourceKey: string * etag: string -> unit

    /// Removes the cached entry for the given resource key.
    member Invalidate: resourceKey: string -> unit

    /// Clears all cached entries and resets statistics.
    member InvalidateAll: unit -> unit

    /// Returns current cache statistics.
    member GetStats: unit -> Async<CacheStats>

    interface IDisposable
