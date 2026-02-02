# Research: SSE Channel Architecture

**Feature**: 006-fix-datastar-basic-tests
**Date**: 2026-02-01

## Executive Summary

The root cause of the 8 failing tests is a **subscription/broadcast timing vulnerability** in the current 5-channel SSE architecture. Fire-and-forget operations may broadcast to a channel when the subscriber's `Subscribe` hasn't been reposted yet, causing broadcasts to queue instead of deliver immediately. The fix is to consolidate to a single global SSE channel per page.

## Current Architecture Analysis

### The MailboxProcessor-Based Channel

```fsharp
type SseChannelMsg =
    | Subscribe of replyChannel: AsyncReplyChannel<SseEvent>
    | Broadcast of SseEvent

let createSseChannel () =
    MailboxProcessor.Start(fun inbox ->
        let subscribers = ResizeArray<AsyncReplyChannel<SseEvent>>()
        let pendingEvents = Queue<SseEvent>()

        let rec loop () =
            async {
                let! msg = inbox.Receive()
                match msg with
                | Subscribe replyChannel ->
                    if pendingEvents.Count > 0 then
                        replyChannel.Reply(pendingEvents.Dequeue())
                    else
                        subscribers.Add(replyChannel)
                | Broadcast event ->
                    if subscribers.Count > 0 then
                        let subscriber = subscribers.[0]
                        subscribers.RemoveAt(0)
                        subscriber.Reply(event)
                    else
                        pendingEvents.Enqueue(event)
                return! loop ()
            }
        loop ())
```

### Key Insight: One-to-One Matching

The channel implements **one-to-one event delivery**:
- On `Subscribe`: If events are pending, immediately deliver one; otherwise wait
- On `Broadcast`: If subscribers waiting, wake one and remove from list; otherwise queue

This model requires precise timing coordination between the subscriber's loop (posting `Subscribe`) and broadcasters (posting `Broadcast`).

### The Timing Vulnerability

The GET endpoint runs an infinite loop:

```fsharp
while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
    let! event = channel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask
    do! writeSseEvent ctx event
```

**The race condition:**
1. Subscriber receives an event via `PostAndAsyncReply`
2. Subscriber writes event to response stream
3. Subscriber loops back to post another `Subscribe`
4. **During this gap**, if a `Broadcast` arrives, the `subscribers` list is empty
5. Broadcast gets queued in `pendingEvents`
6. Subscriber posts `Subscribe`, receives queued event
7. But if multiple broadcasts arrive during the gap, order/timing becomes unpredictable

## Why Each Test Category Fails

### BulkUpdateTests (4 failures)

**Scenario**: User loads users table, selects checkboxes, clicks "Activate Selected"

1. `GET /users` establishes SSE connection to `usersChannel`
2. `PUT /users/bulk` broadcasts updated table to `usersChannel`
3. If timing gap exists, broadcast queues instead of delivering
4. Test waits 500ms but DOM shows old status values
5. Assertion fails

### SearchFilterTests (2 failures)

**Scenario**: User loads fruits list, types search query

1. `GET /fruits` (no query) establishes SSE connection to `fruitsChannel`
2. `GET /fruits?q=ap` broadcasts filtered list to `fruitsChannel`
3. Same timing vulnerability
4. Test finds 22 items instead of filtered subset
5. Assertion fails

### ClickToEditTests (2 failures)

**Scenario**: User edits contact, saves, refreshes page

1. `GET /contacts/1` establishes SSE connection
2. Edit/save operations broadcast through `contactChannel`
3. On refresh, connection closes and re-opens
4. If save broadcast was queued/lost, data may not persist correctly
5. Second load shows original data
6. Assertion fails

## Decision: Single Global SSE Channel

### Chosen Approach

Replace 5 per-resource channels with 1 global channel:

```fsharp
// BEFORE: 5 channels
let contactChannel = createSseChannel ()
let fruitsChannel = createSseChannel ()
let itemsChannel = createSseChannel ()
let usersChannel = createSseChannel ()
let registrationChannel = createSseChannel ()

// AFTER: 1 channel
let globalChannel = createSseChannel ()
```

### Rationale

1. **Eliminates timing races**: One subscriber, one channel, deterministic delivery
2. **Respects browser limits**: HTTP/1.1 allows ~6 connections; 1 SSE connection leaves 5 for other requests
3. **Matches Datastar best practices**: Hypermedia updates should flow through a unified channel
4. **Simplifies code**: Remove per-resource channel management

### Alternatives Considered

| Alternative | Rejected Because |
|-------------|------------------|
| Fix timing with locks/semaphores | Adds complexity; doesn't address browser connection limits |
| Use ASP.NET Core SignalR | Overkill for this sample; not the Frank + Datastar pattern |
| Multiple SSE endpoints with shared state | Still has connection limit issue; complex state management |

## Implementation Requirements

### Changes to Program.fs

1. **Delete**: 5 individual channel declarations
2. **Add**: 1 global channel declaration
3. **Update**: All `GET /resource` handlers to subscribe to global channel
4. **Update**: All fire-and-forget handlers to broadcast to global channel
5. **Keep unchanged**: `SseEvent`, `SseChannelMsg`, `createSseChannel`, render functions

### Changes to index.html

1. **Modify**: Page needs single SSE connection on load (or on first interaction)
2. **Remove**: Per-resource "Load X" buttons that each establish separate SSE connections
3. **Alternative**: Keep buttons but have them trigger fire-and-forget loads through the single channel

### Test Impact

No test changes required. Tests will pass once the channel consolidation is complete because:
- Same DOM selectors
- Same user interactions
- Same expected outcomes
- Only the delivery mechanism changes (reliable single channel vs unreliable multi-channel)
