namespace Frank.Datastar.Tests

open System.Threading.Tasks
open NUnit.Framework

/// Tests for state isolation between features (US4)
/// Verifies that registration form data does not leak into contact display
[<TestFixture>]
type StateIsolationTests() =
    inherit TestBase()

    /// Loads the contact via SSE
    member private this.LoadContact() : Task =
        task {
            let loadButton = this.Page.Locator("button:has-text('Load Contact')")
            do! loadButton.ClickAsync()
            do! TestHelpers.waitForVisible this.Page "#contact-view p" this.TimeoutMs
        }

    /// Loads the registration form via SSE
    member private this.LoadRegistrationForm() : Task =
        task {
            let loadButton = this.Page.Locator("button:has-text('Show Registration Form')")
            do! loadButton.ClickAsync()
            do! TestHelpers.waitForVisible this.Page "#registration-form input" this.TimeoutMs
        }

    /// Fills the registration form fields
    member private this.FillRegistrationForm(email: string, firstName: string, lastName: string) : Task =
        task {
            let emailInput = this.Page.Locator("#registration-form input[type='email']")
            let firstNameInput = this.Page.Locator("#registration-form input[type='text']").First
            let lastNameInput = this.Page.Locator("#registration-form input[type='text']").Nth(1)

            do! emailInput.FillAsync(email)
            do! firstNameInput.FillAsync(firstName)
            do! lastNameInput.FillAsync(lastName)

            // Wait for any debounced validation
            do! Task.Delay(600)
        }

    /// Gets the contact display content
    member private this.GetContactContent() : Task<string> =
        task {
            let contactView = this.Page.Locator("#contact-view")
            let! content = contactView.TextContentAsync()
            return content
        }

    [<Test>]
    member this.``Registration data does not affect contact display``() : Task =
        task {
            // Load contact first
            do! this.LoadContact ()

            // Verify contact shows original email
            let! initialContent = this.GetContactContent ()
            Assert.That(
                initialContent,
                Does.Contain("joe@smith.org"),
                "Contact should initially show 'joe@smith.org'"
            )

            // Load registration form
            do! this.LoadRegistrationForm ()

            // Fill registration with different data
            do! this.FillRegistrationForm ("test@isolation.com", "Test", "User")

            // Check contact display again - should NOT show registration email
            let! contactContent = this.GetContactContent ()

            Assert.That(
                contactContent,
                Does.Not.Contain("test@isolation.com"),
                "Contact display should NOT show registration email 'test@isolation.com'"
            )

            Assert.That(
                contactContent,
                Does.Contain("joe@smith.org"),
                "Contact display should still show original email 'joe@smith.org'"
            )
        }

    [<Test>]
    member this.``Contact data preserved after registration interaction``() : Task =
        task {
            // Load contact
            do! this.LoadContact ()

            // Verify original contact email
            let! initialContent = this.GetContactContent ()
            Assert.That(
                initialContent,
                Does.Contain("joe@smith.org"),
                "Contact should show 'joe@smith.org'"
            )

            // Load and interact with registration form
            do! this.LoadRegistrationForm ()
            do! this.FillRegistrationForm ("different@email.com", "Different", "Person")

            // Verify contact still shows original data
            let! finalContent = this.GetContactContent ()

            Assert.That(
                finalContent,
                Does.Contain("joe@smith.org"),
                "Contact email should remain 'joe@smith.org' after registration interaction"
            )

            Assert.That(
                finalContent,
                Does.Contain("Joe"),
                "Contact first name should remain 'Joe' after registration interaction"
            )

            Assert.That(
                finalContent,
                Does.Contain("Smith"),
                "Contact last name should remain 'Smith' after registration interaction"
            )
        }

    [<Test>]
    member this.``Contact first name not affected by registration first name``() : Task =
        task {
            // Load both contact and registration
            do! this.LoadContact ()
            do! this.LoadRegistrationForm ()

            // Fill registration with a distinctive first name
            let registrationFirstNameInput = this.Page.Locator("#registration-form input[type='text']").First
            do! registrationFirstNameInput.FillAsync("RegistrationOnlyName")

            // Wait for any updates
            do! Task.Delay(600)

            // Verify contact does not show the registration name
            let! contactContent = this.GetContactContent ()

            Assert.That(
                contactContent,
                Does.Not.Contain("RegistrationOnlyName"),
                "Contact should NOT contain registration first name 'RegistrationOnlyName'"
            )
        }
