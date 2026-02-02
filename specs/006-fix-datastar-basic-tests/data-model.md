# Data Model: SSE Channel Architecture

**Feature**: 006-fix-datastar-basic-tests
**Date**: 2026-02-01

## Overview

This document describes the data model for the SSE channel pub/sub system. The key change is consolidating from 5 per-resource channels to 1 global channel.

## Core Types (Unchanged)

These types remain unchanged from the current implementation:

### SseEvent

Discriminated union representing events that can be sent over SSE:

```
SseEvent
├── PatchElements(html: string)    - Replace/insert HTML content
├── RemoveElement(selector: string) - Remove element by CSS selector
├── PatchSignals(json: string)     - Update Datastar signals
└── Close                          - Close the SSE connection
```

### SseChannelMsg

Messages processed by the MailboxProcessor:

```
SseChannelMsg
├── Subscribe(replyChannel: AsyncReplyChannel<SseEvent>)
└── Broadcast(event: SseEvent)
```

## Channel Architecture

### Before (Current)

```
┌─────────────────────────────────────────────────────────────┐
│                     Browser Page                             │
├─────────────────────────────────────────────────────────────┤
│  SSE Conn 1    SSE Conn 2    SSE Conn 3    SSE Conn 4    SSE Conn 5  │
│      ↓              ↓              ↓              ↓              ↓     │
└─────────────────────────────────────────────────────────────┘
      ↓              ↓              ↓              ↓              ↓
┌─────────┐    ┌─────────┐    ┌─────────┐    ┌─────────┐    ┌─────────┐
│contact  │    │fruits   │    │items    │    │users    │    │registr. │
│Channel  │    │Channel  │    │Channel  │    │Channel  │    │Channel  │
└─────────┘    └─────────┘    └─────────┘    └─────────┘    └─────────┘
```

**Problems:**
- 5 concurrent SSE connections per page
- Each channel has independent subscription state
- Timing races between Subscribe and Broadcast

### After (Proposed)

```
┌─────────────────────────────────────────────────────────────┐
│                     Browser Page                             │
├─────────────────────────────────────────────────────────────┤
│                       SSE Connection                         │
│                            ↓                                 │
└─────────────────────────────────────────────────────────────┘
                             ↓
                    ┌─────────────────┐
                    │  globalChannel   │
                    │                 │
                    │ ← Contact ops   │
                    │ ← Fruit ops     │
                    │ ← Item ops      │
                    │ ← User ops      │
                    │ ← Registr. ops  │
                    └─────────────────┘
```

**Benefits:**
- 1 SSE connection per page
- Single subscription state
- Deterministic event delivery

## Domain Entities (Unchanged)

These in-memory data structures remain unchanged:

### Contact

```
Contact {
    Id: int          (primary key)
    FirstName: string
    LastName: string
    Email: string
}
```

Storage: `Dictionary<int, Contact>` with seed data (Id=1, Joe Smith)

### User

```
User {
    Id: int          (primary key)
    Name: string
    Email: string
    Status: UserStatus (Active | Inactive)
}
```

Storage: `Dictionary<int, User>` with 4 seed records

### Item

```
Item {
    Id: int          (primary key)
    Name: string
}
```

Storage: `ResizeArray<Item>` with 4 seed records

### Registration

```
Registration {
    Id: int          (primary key, auto-increment)
    Email: string
    FirstName: string
    LastName: string
}
```

Storage: `ResizeArray<Registration>` (starts empty)

### Fruit

Static list of 22 strings (Apple, Apricot, Banana, etc.)

## State Transitions

### SSE Channel State

```
┌─────────────────┐
│     Empty       │ ← Initial state
│  subscribers=0  │
│  pending=0      │
└────────┬────────┘
         │ Subscribe
         ↓
┌─────────────────┐
│    Waiting      │ ← 1 subscriber waiting
│  subscribers=1  │
│  pending=0      │
└────────┬────────┘
         │ Broadcast
         ↓
┌─────────────────┐
│   Delivered     │ ← Event sent to subscriber
│  subscribers=0  │
│  pending=0      │
└────────┬────────┘
         │ (subscriber loops, posts new Subscribe)
         ↓
       [Waiting]
```

**Edge case - Broadcast before Subscribe:**

```
┌─────────────────┐
│     Empty       │
└────────┬────────┘
         │ Broadcast (no subscribers)
         ↓
┌─────────────────┐
│    Queued       │ ← Event waiting
│  subscribers=0  │
│  pending=1      │
└────────┬────────┘
         │ Subscribe
         ↓
┌─────────────────┐
│   Delivered     │ ← Queued event sent immediately
│  subscribers=0  │
│  pending=0      │
└─────────────────┘
```

## Validation Rules

### Contact Signals

- `firstName`: Required (not empty)
- `lastName`: Required (not empty)
- `email`: Required, must contain "@"

### Registration Signals

- `email`: Required, must contain "@"
- `firstName`: Required (not empty)
- `lastName`: Required (not empty)
- `email`: Must be unique (no duplicate registrations)

### Bulk Update Signals

- `selections`: Boolean array matching user count
- Only selected users (true values) are updated
