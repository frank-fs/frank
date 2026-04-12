---
source: "github issue #285"
title: "Frank.Analyzers — compile-time validation for statechart usage"
milestone: "v7.4.0"
state: "OPEN"
type: spec
---

# Frank.Analyzers — compile-time validation for statechart usage

> Extracted from [frank-fs/frank#285](https://github.com/frank-fs/frank/issues/285)

**Probability of Successful Implementation: ~85%**
 
Frank.Analyzers already exists as a shipping library with FRANK001 (duplicate HTTP method handler detection), working IDE integration (Ionide, VS, Rider), and proven FCS typed/untyped tree traversal for CE operation detection. The infrastructure — packaging, FCS API usage, `fsharp-analyzers` tooling, NuGet distribution — is solved. This issue adds **statechart-specific rules** to the existing library, not a greenfield project. The remaining risk is purely in the pattern-matching logic for each new rule. Cross-symbol analysis for FRANK101 and FRANK102 (resolving relationships between multiple `statefulResource` blocks) is the most complex new capability, but the existing duplicate-handler analyzer already demonstrates CE operation traversal within a single resource, so extending to cross-resource analysis is incremental.
 
**To raise to ~90%:** Specify the naming convention the analyzer uses to detect generated event DU types (e.g., `*.Generated` module with `[<RequireQualifiedAccess>]` DU). Clarify FRANK104's scope for route parameter edge cases. Decide on diagnostic code numbering (FRANK1XX range to avoid collision with existing FRANK0XX rules).
 
## Thesis
 
The existing `Frank.Analyzers` library provides compile-time static analysis for Frank applications (e.g., FRANK001 detects duplicate HTTP method handlers). With the introduction of statechart-backed resources, `childOf` relationships, and generated DU types, new categories of usage errors become possible that compile successfully but fail at runtime. This issue adds **statechart-specific analyzer rules** (FRANK101–FRANK105) to the existing library, extending its CE operation traversal capabilities to cover cross-resource relationships and generated type conventions.
 
## Problem
 
Several categories of statechart usage errors compile successfully but fail at runtime:
 
1. A child resource directly injects `IStatechartsStore`, bypassing the middleware's relationship management
2. A `childOf` declaration references a resource that doesn't exist in the compilation
3. A resource declares both `childOf` and `useStatechart`, claiming dual ownership
4. A child resource's route template has parameter mismatches with its parent's route
5. Event handler code uses raw string literals instead of generated DU constants
 
The existing `Frank.Analyzers` catches analogous errors for basic resources (duplicate handlers). These statechart-specific rules extend the same pattern to the hierarchical statechart layer.
 
## Solution
 
Add new analyzer functions to the existing `Frank.Analyzers` library, using the FRANK1XX diagnostic code range to distinguish from existing FRANK0XX rules. The new rules follow the same patterns already established in the library — FCS typed/untyped tree traversal, CE operation detection, `Message` reporting — extended to cover cross-resource symbol resolution and generated type conventions.
 
The existing library already demonstrates:
* CE operation detection in untyped trees (used by FRANK001 for duplicate handlers)
* IDE integration (Ionide, VS, Rider)
* CLI integration (via `fsharp-analyzers` tool)
* NuGet packaging
 
New capabilities needed:
* **Cross-resource symbol resolution** (FRANK101, FRANK102): resolve `childOf` references to other `statefulResource` declarations within the same project via `FSharpCheckProjectResults`
* **Generated type convention detection** (FRANK105): identify `[<RequireQualifiedAccess>]` DU types in `*.Generated` modules as generated event/state types
 
## Analyzer Rules
 
**FRANK101: Direct IStatechartsStore injection in child resource**
 
A `statefulResource` with a `childOf` declaration must not inject `IStatechartsStore` directly. The middleware manages the parent's store on behalf of the child.
 
```
Trigger: constructor parameter or service resolution call where the
  resolved type's TryGetFullName matches
  "Frank.Statecharts.IStatechartsStore", in a handler type referenced
  by a statefulResource that also declares childOf
Severity: Error
Fix: use StateMachineContext API provided by the middleware
```
 
**FRANK102: childOf references nonexistent resource**
 
A `childOf` declaration must reference a `statefulResource` that exists in the same project.
 
```
Trigger: childOf string literal or type reference with no matching
  statefulResource declaration found via FSharpCheckProjectResults
  symbol resolution
Severity: Error
Fix: correct the reference or add the parent resource definition
```
 
**FRANK103: Dual ownership — childOf and useStatechart on same resource**
 
A resource cannot both own a statechart and be a child of another resource's statechart.
 
```
Trigger: both childOf and useStatechart operations identified in the
  untyped tree within the same statefulResource CE application
Severity: Warning (Error if no deliberate nested-machine pattern exists)
Fix: remove one of the declarations
```
 
**FRANK104: Route parameter mismatch between parent and child**
 
A child resource's route template must include the parent's instance ID parameter.
 
```
Trigger: parent route has {id} but child route does not include {id},
  or child has a parameter not present in parent — detected by parsing
  string literal arguments to route/name CE operations
Severity: Warning
Fix: ensure child route includes parent's instance ID parameter
```
 
**FRANK105: Raw string event name instead of generated DU constant**
 
Event handler code should reference generated DU cases, not raw string literals, when generated types are available in the compilation.
 
```
Trigger: .Send("pick_completed") or similar string literal in a context
  where a generated Event DU exists (detected by checking
  FSharpCheckProjectResults for a [<RequireQualifiedAccess>] DU type
  in a *.Generated module)
Severity: Warning
Fix: use Event.PickCompleted instead of "pick_completed"
```
 
## Acceptance Criteria
 
### AC-1: FRANK101 fires on direct store injection in child resource
 
```
Given: a statefulResource with childOf and a handler type that takes
  IStatechartsStore as a constructor parameter
When: the fsharp-analyzers CLI is run against the project with
  Frank.Analyzers on the --analyzers-path
Then: FRANK101 message is reported at the constructor parameter site
Falsifiable by: wrapping IStatechartsStore in a helper type and verifying
  the analyzer still fires (tests that the check uses TryGetFullName
  resolution, not just the parameter name)
```
 
### AC-2: FRANK102 fires on nonexistent parent reference
 
```
Given: a statefulResource with childOf "nonexistent-resource"
When: the fsharp-analyzers CLI is run against the project
Then: FRANK102 message is reported at the childOf call site
Falsifiable by: adding a statefulResource with the matching name and
  verifying the message disappears
```
 
### AC-3: FRANK103 fires on dual ownership
 
```
Given: a statefulResource with both childOf and useStatechart
When: the fsharp-analyzers CLI is run against the project
Then: FRANK103 message is reported
Falsifiable by: removing either childOf or useStatechart and verifying
  the message disappears
```
 
### AC-4: FRANK104 fires on route parameter mismatch
 
```
Given: parent resource with route "/orders/{id}" and child with route
  "/orders/pick" (missing {id})
When: the fsharp-analyzers CLI is run against the project
Then: FRANK104 message is reported at the child's route declaration
Falsifiable by: changing child route to "/orders/{id}/pick" and verifying
  the message disappears
```
 
### AC-5: FRANK105 fires on raw string event name
 
```
Given: a handler calling context.Send("pick_completed") when a generated
  OrderStatechart.Generated module with an Event DU exists in the project
When: the fsharp-analyzers CLI is run against the project
Then: FRANK105 message is reported at the string literal
Falsifiable by: changing to context.Send(Event.PickCompleted) and verifying
  the message disappears
```
 
### AC-6: Analyzer package has zero Frank runtime dependencies
 
```
Given: the Frank.Analyzers NuGet package
When: its dependency tree is inspected
Then: it references only FSharp.Analyzers.SDK (and its transitive
  FSharp.Compiler.Service dependency) — no Frank.* packages
Falsifiable by: any Frank.* package appearing in the dependency list
```
 
### AC-7: Analyzer runs in both CLI and editor modes
 
```
Given: Frank.Analyzers with [<CliAnalyzer>] annotated functions
When: the fsharp-analyzers CLI is run against a project with violations
  AND the same project is opened in VS Code with Ionide and the analyzer
  path configured
Then: both environments report the same FRANK0XX messages
Falsifiable by: messages appearing in CLI but not in the editor, or
  vice versa (would indicate the analyzer only supports one mode)
```
 
### AC-8: Analyzer uses TryGetFullName, never FullName
 
```
Given: the Frank.Analyzers source code
When: a code search is performed for ".FullName" on FSharpEntity references
Then: zero occurrences are found — all entity name resolution uses
  TryGetFullName
Falsifiable by: any FSharpEntity.FullName usage in the source
  (this is a known footgun in the FCS API that throws for entities
  without a full name)
```
 
## Dependencies
 
* Depends on: codegen design issue (analyzer validates against generated type conventions — needs to know the `*.Generated` module naming convention)
* Depends on: #273 (childOf CE operation must exist for FRANK101–FRANK104 to have something to analyze)
* Depends on: `FSharp.Analyzers.SDK` (v0.36.0+ — the analyzer framework)
* Independent of: #257 (analyzer doesn't need to understand the algebra — just type names and CE operation patterns)
* Independent of: MSBuild integration (analyzer runs via `fsharp-analyzers` CLI or FSAC, not as part of compilation)
 
## Expert Sources
 
* **ionide/FSharp.Analyzers.SDK** — the F# analyzer framework; provides `CliContext`/`EditorContext` with access to FCS typed/untyped trees, `Message` type for diagnostics, and `[<CliAnalyzer>]`/`[<EditorAnalyzer>]` attributes for registration
* **FSharp.Analyzers.SDK Getting Started Writing guide** — documents the analyzer authoring model, dual analyzer pattern, packaging requirements (publish output must include all dependency assemblies), and the `TryGetFullName` footgun
* **G-Research/fsharp-analyzers** — example production analyzers built on the SDK; demonstrates pattern matching against FCS typed trees for real-world validation rules
