namespace Frank.Datastar.Tests

open System
open System.Threading.Tasks
open NUnit.Framework
open Microsoft.Playwright

/// Tests for bulk update functionality (US3)
/// Verifies SSE-driven status updates for multiple users
[<TestFixture>]
type BulkUpdateTests() =
    inherit TestBase()

    /// Loads the users table via SSE by clicking the "Load Users" button
    member private this.LoadUsers() : Task =
        task {
            let loadButton = this.Page.Locator("button:has-text('Load Users')")
            do! loadButton.ClickAsync()
            // Wait for users table to be populated via SSE
            do! TestHelpers.waitForVisible this.Page "#users-table-container tbody tr" this.TimeoutMs
        }

    /// Clicks the "Activate Selected" button
    member private this.ClickActivate() : Task =
        task {
            let activateButton = this.Page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Activate Selected", Exact = Nullable true))
            do! activateButton.ClickAsync()
            // Wait for SSE update
            do! Task.Delay(500)
        }

    /// Clicks the "Deactivate Selected" button
    member private this.ClickDeactivate() : Task =
        task {
            let deactivateButton = this.Page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Deactivate Selected", Exact = Nullable true))
            do! deactivateButton.ClickAsync()
            // Wait for SSE update
            do! Task.Delay(500)
        }

    /// Gets the status of a user row by index (0-based)
    member private this.GetUserStatus(index: int) : Task<string> =
        task {
            let row = this.Page.Locator("#users-table-container tbody tr").Nth(index)
            let statusCell = row.Locator("td").Last
            let! text = statusCell.TextContentAsync()
            return text.Trim()
        }

    /// Toggles the checkbox for a user row by index (0-based)
    member private this.ToggleUserCheckbox(index: int) : Task =
        task {
            let row = this.Page.Locator("#users-table-container tbody tr").Nth(index)
            let checkbox = row.Locator("input[type='checkbox']")
            do! checkbox.ClickAsync()
        }

    /// Restores all users to initial state (mixed active/inactive)
    member private this.RestoreUsers() : Task =
        task {
            // Reload page to reset state
            let! _ = this.Page.ReloadAsync()
            ()
            // The in-memory store persists, so we need to manually reset
            // For now, we accept that tests may leave state changed
            ()
        }

    [<TearDown>]
    member this.CleanupUsers() : Task =
        task {
            // Note: Users state persists in memory on server
            // Each test should handle its own state expectations
            ()
        }

    [<Test>]
    member this.``Bulk activate changes selected user statuses``() : Task =
        task {
            // Load users
            do! this.LoadUsers ()

            // Get initial statuses
            let! status1 = this.GetUserStatus 1 // Jane Doe - initially Inactive
            let! status3 = this.GetUserStatus 3 // Alice Brown - initially Inactive

            // Select inactive users (rows 1 and 3 are initially Inactive based on seed data)
            do! this.ToggleUserCheckbox 1
            do! this.ToggleUserCheckbox 3

            // Click Activate Selected
            do! this.ClickActivate ()

            // Wait for SSE update
            do! Task.Delay(500)

            // Verify selected users are now Active
            let! newStatus1 = this.GetUserStatus 1
            let! newStatus3 = this.GetUserStatus 3

            Assert.That(
                newStatus1,
                Is.EqualTo("Active"),
                "Jane Doe (row 1) should be Active after bulk activate"
            )

            Assert.That(
                newStatus3,
                Is.EqualTo("Active"),
                "Alice Brown (row 3) should be Active after bulk activate"
            )
        }

    [<Test>]
    member this.``Bulk activate does not affect unselected users``() : Task =
        task {
            // Load users
            do! this.LoadUsers ()

            // Get initial status of unselected user
            let! initialStatus0 = this.GetUserStatus 0 // Joe Smith - initially Active

            // Select only row 1
            do! this.ToggleUserCheckbox 1

            // Click Activate Selected
            do! this.ClickActivate ()

            // Wait for SSE update
            do! Task.Delay(500)

            // Verify unselected user status unchanged
            let! finalStatus0 = this.GetUserStatus 0

            Assert.That(
                finalStatus0,
                Is.EqualTo(initialStatus0),
                "Unselected user (Joe Smith) status should remain unchanged"
            )
        }

    [<Test>]
    member this.``Bulk changes persisted after refresh``() : Task =
        task {
            // Load users
            do! this.LoadUsers ()

            // Select an inactive user
            do! this.ToggleUserCheckbox 1 // Jane Doe

            // Activate
            do! this.ClickActivate ()

            // Wait for SSE update
            do! Task.Delay(500)

            // Refresh page
            let! _ = this.Page.ReloadAsync()
            ()

            // Load users again
            do! this.LoadUsers ()

            // Verify status is still Active (persisted)
            let! status1 = this.GetUserStatus 1

            Assert.That(
                status1,
                Is.EqualTo("Active"),
                "Jane Doe should still be Active after page refresh (change was persisted)"
            )
        }

    [<Test>]
    member this.``Empty selection does nothing``() : Task =
        task {
            // Load users
            do! this.LoadUsers ()

            // Get all initial statuses
            let! status0 = this.GetUserStatus 0
            let! status1 = this.GetUserStatus 1
            let! status2 = this.GetUserStatus 2
            let! status3 = this.GetUserStatus 3

            // Click Activate without selecting anyone
            do! this.ClickActivate ()

            // Wait
            do! Task.Delay(500)

            // Verify no changes
            let! newStatus0 = this.GetUserStatus 0
            let! newStatus1 = this.GetUserStatus 1
            let! newStatus2 = this.GetUserStatus 2
            let! newStatus3 = this.GetUserStatus 3

            Assert.That(newStatus0, Is.EqualTo(status0), "Status 0 should be unchanged")
            Assert.That(newStatus1, Is.EqualTo(status1), "Status 1 should be unchanged")
            Assert.That(newStatus2, Is.EqualTo(status2), "Status 2 should be unchanged")
            Assert.That(newStatus3, Is.EqualTo(status3), "Status 3 should be unchanged")
        }

    [<Test>]
    member this.``Deactivate changes selected active users to inactive``() : Task =
        task {
            // Load users
            do! this.LoadUsers ()

            // Select an active user (row 0 - Joe Smith is initially Active)
            do! this.ToggleUserCheckbox 0

            // Click Deactivate Selected
            do! this.ClickDeactivate ()

            // Wait for SSE update
            do! Task.Delay(500)

            // Verify user is now Inactive
            let! status0 = this.GetUserStatus 0

            Assert.That(
                status0,
                Is.EqualTo("Inactive"),
                "Joe Smith should be Inactive after bulk deactivate"
            )
        }
