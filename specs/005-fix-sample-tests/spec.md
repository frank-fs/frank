# Feature Specification: Enhanced Sample Test Validation

**Feature Branch**: `005-fix-sample-tests`
**Created**: 2026-01-27
**Status**: Draft

## Clarifications

### Session 2026-01-27

- Q: Should test scripts only detect bugs or also fix the underlying sample bugs? → A: Only detect and report bugs (tests fail when bugs are present); fixing sample bugs is out of scope
- Q: How should tests handle data state between test runs? → A: Tests are sequential and stateful, representing a single user; each test builds on state from previous tests. Samples intentionally don't support concurrent users (single SSE channel). Tests verify known behavior, not complex scenarios.

**Input**: User description: "Enhance the sample/Frank.Datastar.*/test.sh script to more accurately validate the behaviors. When testing manually, I noticed: 1) click-to-edit does not present the current values nor save the updated values, 2) search filtering clears the list entirely, 3) bulk updates doesn't appear to do anything, etc. Different samples break in different ways. Hox appears the most broken, which could be related to incorrectly not awaiting renders. Basic seems the most correct, but it also shares field values across click-to-edit and registration form validation."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate Click-to-Edit Behavior (Priority: P1)

As a developer running the test scripts, I want the tests to verify that the click-to-edit feature correctly displays current values when entering edit mode and persists updated values after saving, so that I can catch regressions where the edit form shows empty or stale data.

**Why this priority**: Click-to-edit is a core interactive feature. If it doesn't show current values or save updates, the feature is fundamentally broken. This is the most critical user-facing behavior to validate.

**Independent Test**: Can be fully tested by requesting the edit form for a known contact, verifying the form contains the expected current field values, submitting updated values, and then verifying a subsequent read returns the updated values.

**Acceptance Scenarios**:

1. **Given** a contact exists with known first name, last name, and email, **When** the edit form is requested, **Then** the response contains input fields pre-populated with the contact's current values
2. **Given** a contact exists with known values, **When** updated values are submitted, **Then** a subsequent read of that contact shows the updated values (not the original values)
3. **Given** a contact exists, **When** the edit form is requested and cancelled without saving, **Then** a subsequent read shows the original unchanged values

---

### User Story 2 - Validate Search Filtering Results (Priority: P1)

As a developer running the test scripts, I want the tests to verify that search filtering actually returns matching results (not an empty list), so that I can catch regressions where the search feature incorrectly clears or fails to populate results.

**Why this priority**: Search is a fundamental feature for users to find data. If searching clears the list entirely instead of filtering, users cannot use the application effectively.

**Independent Test**: Can be fully tested by searching for a known term that should match existing items and verifying the response contains the expected matching items.

**Acceptance Scenarios**:

1. **Given** a list contains items including "Apple", "Apricot", and "Banana", **When** searching with query "ap" (case-insensitive), **Then** the response contains "Apple" and "Apricot" but not "Banana"
2. **Given** a list contains items, **When** searching with a query that matches nothing, **Then** the response indicates no results (empty list or "no results" message) rather than an error
3. **Given** a list contains items, **When** the search query is cleared, **Then** the full unfiltered list is displayed

---

### User Story 3 - Validate Bulk Update Operations (Priority: P1)

As a developer running the test scripts, I want the tests to verify that bulk update operations actually modify the selected items' data, so that I can catch regressions where bulk operations appear to succeed but don't change anything.

**Why this priority**: Bulk operations are efficiency features. If they don't actually update data, users waste time believing operations completed when they did not.

**Independent Test**: Can be fully tested by reading initial state of multiple items, performing a bulk update on selected items, and verifying that only the selected items show the updated state.

**Acceptance Scenarios**:

1. **Given** users with statuses "inactive", "active", "inactive", "active", **When** a bulk activate is performed selecting users 2 and 4, **Then** a subsequent read shows users 2 and 4 as "active" (note: user 2 and 4 were already active, so also test users that change)
2. **Given** users with statuses "inactive", "active", "inactive", "active", **When** a bulk activate is performed selecting users 1 and 3, **Then** a subsequent read shows users 1 and 3 changed from "inactive" to "active"
3. **Given** users in various states, **When** a bulk update is performed with no items selected, **Then** no data changes occur

---

### User Story 4 - Validate State Isolation Between Features (Priority: P2)

As a developer running the test scripts, I want the tests to verify that different features maintain separate state, so that I can catch regressions where form fields or data stores are incorrectly shared.

**Why this priority**: State isolation prevents confusing user experiences where entering data in one form unexpectedly affects another. This is important for correctness but less critical than individual features working.

**Independent Test**: Can be fully tested by entering data in one form (e.g., registration), then checking that another form (e.g., click-to-edit) does not show that data.

**Acceptance Scenarios**:

1. **Given** the registration form and click-to-edit form exist, **When** the user enters "test@example.com" in the registration email field, **Then** the click-to-edit form does not show "test@example.com" in any field
2. **Given** a contact has email "original@example.com", **When** the registration form is used with "different@example.com", **Then** the contact's email remains "original@example.com"

---

### User Story 5 - Detect Sample-Specific Failures (Priority: P2)

As a developer running the test scripts, I want the tests to report which specific sample (Basic, Hox, Oxpecker) fails which tests, so that I can identify rendering or implementation differences between view engines.

**Why this priority**: Different samples may have different failure modes. Knowing which sample fails which test accelerates debugging and prevents incorrect assumptions about shared behavior.

**Independent Test**: Can be fully tested by running the same test suite against each sample and comparing results.

**Acceptance Scenarios**:

1. **Given** the test script runs against a sample, **When** tests complete, **Then** the output clearly identifies which sample was tested and provides a summary of pass/fail counts
2. **Given** multiple samples exist, **When** a test fails on one sample but passes on another, **Then** the test output distinguishes the per-sample results

---

### User Story 6 - Validate Async Rendering Completion (Priority: P3)

As a developer running the test scripts, I want the tests to wait appropriately for async rendering to complete before asserting content, so that I can catch timing-related failures especially in samples like Hox that may have different rendering timing.

**Why this priority**: Flaky tests due to timing reduce confidence in test results. However, this is lower priority than validating correctness because timing issues may not indicate actual bugs.

**Independent Test**: Can be fully tested by introducing appropriate waits or retries for content checks and verifying tests pass consistently across multiple runs.

**Acceptance Scenarios**:

1. **Given** an SSE endpoint returns content asynchronously, **When** the test checks for expected content, **Then** the test waits for the content to appear or times out with a clear message
2. **Given** a test that previously failed intermittently, **When** run 5 times consecutively, **Then** the test produces consistent results (all pass or all fail, not a mix)

---

### Edge Cases

- What happens when the server returns unexpected content type? → Test reports failure with actual content received
- How does the test handle network timeouts vs. application errors? → Both are reported as failures with descriptive messages
- What if expected seed data is missing or corrupted? → Early test failures indicate missing prerequisites; tests do not attempt recovery

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Test scripts MUST verify that edit forms display current entity values (not empty fields)
- **FR-002**: Test scripts MUST verify that saved edits persist to subsequent reads
- **FR-003**: Test scripts MUST verify that search returns matching results (not empty lists when matches exist)
- **FR-004**: Test scripts MUST verify that bulk operations actually modify selected items' data
- **FR-005**: Test scripts MUST verify state isolation between independent features (e.g., registration form does not affect click-to-edit values)
- **FR-006**: Test scripts MUST clearly report which sample is being tested in output
- **FR-007**: Test scripts MUST provide a summary of pass/fail counts at completion
- **FR-008**: Test scripts MUST handle async content appropriately (wait for content rather than fail on timing)
- **FR-009**: Test scripts MUST exit with non-zero status if any test fails
- **FR-010**: Test scripts MUST be runnable independently for each sample (Basic, Hox, Oxpecker)

### Key Entities

- **Sample Application**: A self-contained demo application (Basic, Hox, or Oxpecker) that implements standard features using different view engines
- **Test Script**: A bash script that validates sample application behavior through HTTP requests
- **Contact**: An entity with first name, last name, and email that supports view and click-to-edit operations
- **Fruit List**: A searchable list of fruit names used for search filtering validation
- **User**: An entity with an active/inactive status used for bulk update validation
- **Registration Form**: A form for creating new users, should be isolated from other forms

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of known behavioral issues (click-to-edit values, search clearing, bulk updates) are detected by the enhanced tests when the bugs are present
- **SC-002**: Tests run to completion in under 30 seconds per sample application
- **SC-003**: Test scripts produce consistent results across 5 consecutive runs (no flaky failures due to timing)
- **SC-004**: All three samples (Basic, Hox, Oxpecker) can be tested with the same test structure, with clear per-sample reporting
- **SC-005**: A developer can identify from the test output alone which specific feature is broken in which sample

## Assumptions

- Sample applications have seed data pre-loaded (at least one contact with known values, fruits list with known items, users with known statuses)
- The server is running before tests are executed (tests may fail-fast if server is unavailable)
- HTTP status codes alone are not sufficient for validation; response content must also be verified
- The test scripts will use curl or similar tools available in standard bash environments
- SSE (Server-Sent Events) endpoints may require special handling for timeouts as the connection stays open
- Tests are sequential and stateful: each test may depend on state changes from previous tests
- Single-user model: samples use a single SSE channel and don't support concurrent users
- Tests verify known, expected behavior - not complex concurrent or edge-case scenarios

## Out of Scope

- Fixing bugs in the sample applications (Basic, Hox, Oxpecker) - tests only detect and report issues
- Modifying the sample application source code
- Adding new features to the sample applications
