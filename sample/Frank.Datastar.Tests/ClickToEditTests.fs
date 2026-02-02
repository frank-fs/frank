namespace Frank.Datastar.Tests

open System.Threading.Tasks
open NUnit.Framework
open Microsoft.Playwright

/// Tests for click-to-edit functionality (US1)
/// Verifies SSE-driven UI updates for contact editing
[<TestFixture>]
type ClickToEditTests() =
    inherit TestBase()

    /// Loads the contact via SSE by clicking the "Load Contact" button
    member private this.LoadContact() : Task =
        task {
            let loadButton = this.Page.Locator("button:has-text('Load Contact')")
            do! loadButton.ClickAsync()
            // Wait for contact view to be populated via SSE
            do! TestHelpers.waitForVisible this.Page "#contact-view p" this.TimeoutMs
        }

    /// Clicks the Edit button to switch to edit mode
    member private this.ClickEdit() : Task =
        task {
            let editButton = this.Page.Locator("#contact-view button:has-text('Edit')")
            do! editButton.ClickAsync()
            // Wait for edit form to appear via SSE (inputs appear)
            do! TestHelpers.waitForVisible this.Page "#contact-view input" this.TimeoutMs
        }

    /// Clicks the Save button to persist changes
    member private this.ClickSave() : Task =
        task {
            let saveButton = this.Page.Locator("#contact-view button:has-text('Save')")
            do! saveButton.ClickAsync()
            // Wait for view mode to return via SSE (Edit button reappears)
            do! TestHelpers.waitForVisible this.Page "#contact-view button:has-text('Edit')" this.TimeoutMs
        }

    /// Restores contact to original values after test
    member private this.RestoreContact() : Task =
        task {
            // Load contact if not already loaded
            let! hasEditButton = TestHelpers.isVisible this.Page "#contact-view button:has-text('Edit')"

            if not hasEditButton then
                do! this.LoadContact ()

            // Click edit
            do! this.ClickEdit ()

            // Fill with original values
            let firstNameInput = this.Page.Locator("#contact-view input").First
            let lastNameInput = this.Page.Locator("#contact-view input").Nth(1)
            let emailInput = this.Page.Locator("#contact-view input").Nth(2)

            do! firstNameInput.FillAsync("Joe")
            do! lastNameInput.FillAsync("Smith")
            do! emailInput.FillAsync("joe@smith.org")

            // Save
            do! this.ClickSave ()
        }

    [<TearDown>]
    member this.CleanupContact() : Task =
        task {
            // Restore contact to original state after each test
            try
                do! this.RestoreContact ()
            with _ ->
                // Ignore cleanup errors
                ()
        }

    [<Test>]
    member this.``Edit form shows current values``() : Task =
        task {
            // Load contact
            do! this.LoadContact ()

            // Verify initial display shows Joe
            do! TestHelpers.waitForTextContains this.Page "#contact-view" "Joe" this.TimeoutMs

            // Click Edit
            do! this.ClickEdit ()

            // Verify edit form input has "Joe" value
            let firstNameInput = this.Page.Locator("#contact-view input").First
            let! value = firstNameInput.InputValueAsync()

            Assert.That(value, Is.EqualTo("Joe"), "Edit form should display current firstName value 'Joe'")
        }

    [<Test>]
    member this.``Saved edits appear in display``() : Task =
        task {
            // Load contact
            do! this.LoadContact ()

            // Click Edit
            do! this.ClickEdit ()

            // Change first name to "Updated"
            let firstNameInput = this.Page.Locator("#contact-view input").First
            do! firstNameInput.FillAsync("Updated")

            // Save
            do! this.ClickSave ()

            // Verify display shows "Updated" via SSE
            do! TestHelpers.waitForTextContains this.Page "#contact-view" "Updated" this.TimeoutMs

            let! content = this.Page.Locator("#contact-view").TextContentAsync()
            Assert.That(content, Does.Contain("Updated"), "Display should show updated firstName 'Updated' after save")
        }

    [<Test>]
    member this.``Saved edits persisted after refresh``() : Task =
        task {
            // Load contact
            do! this.LoadContact ()

            // Click Edit
            do! this.ClickEdit ()

            // Change first name to "Persisted"
            let firstNameInput = this.Page.Locator("#contact-view input").First
            do! firstNameInput.FillAsync("Persisted")

            // Save
            do! this.ClickSave ()

            // Wait for update to be visible
            do! TestHelpers.waitForTextContains this.Page "#contact-view" "Persisted" this.TimeoutMs

            // Refresh page
            let! _ = this.Page.ReloadAsync()
            ()

            // Load contact again
            do! this.LoadContact ()

            // Verify persisted value is displayed (not reverted to "Joe")
            do! TestHelpers.waitForTextContains this.Page "#contact-view" "Persisted" this.TimeoutMs

            let! content = this.Page.Locator("#contact-view").TextContentAsync()
            Assert.That(
                content,
                Does.Contain("Persisted"),
                "Display should show persisted firstName 'Persisted' after page refresh"
            )
        }

    [<Test>]
    member this.``Cancel edit returns to view mode without saving``() : Task =
        task {
            // Load contact
            do! this.LoadContact ()

            // Click Edit
            do! this.ClickEdit ()

            // Change first name
            let firstNameInput = this.Page.Locator("#contact-view input").First
            do! firstNameInput.FillAsync("ShouldNotSave")

            // Click Cancel
            let cancelButton = this.Page.Locator("#contact-view button:has-text('Cancel')")
            do! cancelButton.ClickAsync()

            // Wait for view mode to return
            do! TestHelpers.waitForVisible this.Page "#contact-view button:has-text('Edit')" this.TimeoutMs

            // Verify original value is shown
            do! TestHelpers.waitForTextContains this.Page "#contact-view" "Joe" this.TimeoutMs

            let! content = this.Page.Locator("#contact-view").TextContentAsync()
            Assert.That(
                content,
                Does.Not.Contain("ShouldNotSave"),
                "Display should not show cancelled changes"
            )
        }
