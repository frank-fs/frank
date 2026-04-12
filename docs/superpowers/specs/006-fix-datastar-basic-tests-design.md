---
source: specs/006-fix-datastar-basic-tests
status: complete
type: spec
---

# Feature Specification: Fix Frank.Datastar.Basic Sample Tests

**Feature Branch**: `006-fix-datastar-basic-tests`
**Created**: 2026-02-01
**Status**: Draft
**Input**: User description: "Fix sample/Frank.Datastar.Basic by getting all tests to pass in sample/Frank.Datastar.Tests"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Bulk User Status Update Works (Priority: P1)

As a user managing multiple users, I want to select users and change their status (Active/Inactive) in bulk, so that status changes are reflected immediately without page refresh.

**Why this priority**: Bulk update is a core feature with 4 failing tests. The tests demonstrate that checkbox selections are not properly communicated to the server, causing activate/deactivate operations to have no effect.

**Independent Test**: Can be fully tested by loading the users table, selecting checkboxes, clicking Activate/Deactivate, and verifying status cells update via SSE.

**Acceptance Scenarios**:

1. **Given** the users table is loaded via SSE, **When** I select inactive users (rows 1 and 3) and click "Activate Selected", **Then** those users' status cells display "Active" after SSE update.
2. **Given** the users table is loaded via SSE, **When** I select an active user (row 0) and click "Deactivate Selected", **Then** that user's status cell displays "Inactive" after SSE update.
3. **Given** I have activated a previously inactive user, **When** I refresh the page and reload the users table, **Then** the user's status still shows "Active" (persisted).
4. **Given** the users table is loaded, **When** I click "Activate Selected" without selecting any users, **Then** no user statuses change.
5. **Given** I select only some users, **When** I click Activate/Deactivate, **Then** unselected users' statuses remain unchanged.

---

### User Story 2 - Search Filter Updates List via SSE (Priority: P2)

As a user searching through a list of fruits, I want search results to update in real-time as I type, so that I can quickly find matching items without page refresh.

**Why this priority**: Search filtering has 2 failing tests. The search functionality appears to not send filtered results back through the SSE channel, leaving the list unchanged after typing a search query.

**Independent Test**: Can be fully tested by loading the fruits list, typing a search term, and verifying the list updates to show only matching items.

**Acceptance Scenarios**:

1. **Given** the fruits list is loaded with all 22 items, **When** I type "ap" in the search input, **Then** the list updates via SSE to show only fruits containing "ap" (Apple, Apricot, Grape, Papaya).
2. **Given** I have filtered the list with a search term, **When** I clear the search input, **Then** the full list of 22 fruits is restored via SSE.
3. **Given** the fruits list is loaded, **When** I search for "APPLE" (uppercase), **Then** the list shows "Apple" (case-insensitive match).
4. **Given** the fruits list is loaded, **When** I search for "xyz123nonexistent", **Then** the list shows zero items (empty list).

---

### User Story 3 - Contact Edit Cancel Returns to View Mode (Priority: P3)

As a user editing contact information, I want to cancel my edits and return to view mode, so that unsaved changes are discarded and the original data is displayed.

**Why this priority**: The cancel functionality test is failing. When clicking Cancel, the edit form should revert to display mode showing the original contact data.

**Independent Test**: Can be fully tested by loading a contact, clicking Edit, modifying data, clicking Cancel, and verifying original values are displayed.

**Acceptance Scenarios**:

1. **Given** I am in edit mode for a contact with first name "Joe", **When** I change the first name to "ShouldNotSave" and click Cancel, **Then** the view mode returns showing "Joe" (not "ShouldNotSave").
2. **Given** I am in edit mode, **When** I click Cancel, **Then** the Edit button reappears and input fields are replaced with display text.

---

### User Story 4 - Contact Edits Persist After Refresh (Priority: P3)

As a user saving contact edits, I want my changes to persist, so that after refreshing the page the saved data is still displayed.

**Why this priority**: The persistence test is failing. After saving edits and refreshing, the original (not updated) data appears, suggesting SSE channel state may not be maintained correctly.

**Independent Test**: Can be fully tested by editing and saving a contact, refreshing the page, reloading the contact, and verifying the saved data displays.

**Acceptance Scenarios**:

1. **Given** I edit a contact's first name to "Persisted" and save, **When** I refresh the page and reload the contact via SSE, **Then** the contact displays "Persisted" as the first name.

---

### Edge Cases

- What happens when the SSE connection is lost mid-operation?
- How does the system handle rapid successive search queries (debounce behavior)?
- What happens if multiple browser tabs are editing the same contact?

## Architectural Problem Analysis

The current implementation creates **5 separate SSE channels** (one per resource: contacts, fruits, items, users, registrations). Each "Load X" button establishes its own independent SSE connection. This architecture should be investigated for the following potential issues:

1. **Browser connection limits**: Browsers limit concurrent connections per domain (typically 6 for HTTP/1.1). With 5 long-lived SSE connections, this limit could be reached, causing new connections to queue or fail.

2. **Channel subscription mismatch**: If fire-and-forget operations broadcast to a channel that the client isn't actually subscribed to, or if the subscription mechanism isn't working correctly, updates won't propagate to the UI. For example, "Load Users" establishes an SSE connection, but "Activate Selected" may be broadcasting to a channel the client can't receive from.

3. **MailboxProcessor subscription model**: The current `createSseChannel` implementation uses a queue-based subscription model. If the subscription/broadcast coordination has timing issues or the subscriber isn't correctly registered when broadcasts occur, messages could be lost.

**Required architectural change**: Consolidate to a single SSE channel per page. All operations (contact edits, search filters, bulk updates, etc.) should broadcast through one shared channel that the page subscribes to on load. This respects browser connection limits and ensures all updates flow through a channel the client is actively subscribed to.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST use a single SSE connection per page rather than separate connections per resource.
- **FR-002**: System MUST send bulk status update results through the shared SSE channel so the users table reflects changes without page refresh.
- **FR-003**: System MUST send search filter results through the shared SSE channel so the fruits list updates based on query parameters.
- **FR-004**: System MUST restore original contact data when the Cancel button is clicked during editing.
- **FR-005**: System MUST persist contact edits to the in-memory data store so changes survive page refresh.
- **FR-006**: System MUST ensure all fire-and-forget operations broadcast through the same SSE channel the page is subscribed to.
- **FR-007**: System MUST handle empty search queries by restoring the full unfiltered list.
- **FR-008**: System MUST perform case-insensitive search filtering.

### Key Entities

- **Contact**: User contact information with Id, FirstName, LastName, and Email. Stored in-memory dictionary.
- **User**: User record with Id, Name, Email, and Status (Active/Inactive). Stored in-memory dictionary.
- **Fruit**: Static list of fruit names for search/filter demonstration.
- **SSE Channel**: MailboxProcessor-based pub/sub channel for broadcasting HTML updates to connected clients.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 20 tests in Frank.Datastar.Tests pass when run against Frank.Datastar.Basic sample (currently 12 pass, 8 fail).
- **SC-002**: Bulk user status updates appear within 1 second of clicking Activate/Deactivate buttons.
- **SC-003**: Search filter results appear within 500ms of debounce timeout (300ms debounce + network latency).
- **SC-004**: Cancel button returns to view mode within 500ms showing original data.
- **SC-005**: Saved contact edits remain visible after page refresh and contact reload.

## Research

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
