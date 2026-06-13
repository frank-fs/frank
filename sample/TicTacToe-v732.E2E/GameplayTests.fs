namespace TicTacToe.E2E

open System.Text.Json
open System.Threading.Tasks
open Microsoft.Playwright
open Microsoft.Playwright.NUnit
open NUnit.Framework

/// Minimal gameplay validation — just enough to prove the game plays correctly.
/// Exhaustive game-logic coverage lives upstream in the Engine's own tests; the
/// real target of v7.3.2 is the semantic layer (see SemanticTests).
[<TestFixture>]
type GameplayTests() =
    inherit PlaywrightTest()

    member this.NewContext() : Task<IAPIRequestContext> =
        this.Playwright.APIRequest.NewContextAsync(APIRequestNewContextOptions(BaseURL = Server.Url()))

    [<Test>]
    member this.``new game starts in X turn with all cells empty``() =
        task {
            use! ctx = this.NewContext()
            let! resp = ctx.GetAsync("/games/e2e-new")
            Assert.That(resp.Status, Is.EqualTo 200)
            let! json = resp.JsonAsync()
            let root = json.Value
            Assert.That(root.GetProperty("status").GetString(), Is.EqualTo "XTurn")
            Assert.That(root.GetProperty("currentPlayer").GetString(), Is.EqualTo "X")

            let squares = root.GetProperty("squares")

            for prop in squares.EnumerateObject() do
                Assert.That(prop.Value.ValueKind, Is.EqualTo JsonValueKind.Null, prop.Name)
        }

    [<Test>]
    member this.``a move marks the square and passes the turn``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-move") // GET creates the game (real client flow)

            let! resp =
                ctx.PostAsync(
                    "/games/e2e-move/moves",
                    APIRequestContextOptions(DataObject = {| position = "TopLeft"; player = "X" |})
                )

            Assert.That(resp.Status, Is.EqualTo 200)
            let! json = resp.JsonAsync()
            let root = json.Value
            Assert.That(root.GetProperty("squares").GetProperty("TopLeft").GetString(), Is.EqualTo "X")
            Assert.That(root.GetProperty("status").GetString(), Is.EqualTo "OTurn")
            Assert.That(root.GetProperty("currentPlayer").GetString(), Is.EqualTo "O")
        }

    [<Test>]
    member this.``an out-of-turn move is rejected with 409``() =
        task {
            use! ctx = this.NewContext()
            let! _ = ctx.GetAsync("/games/e2e-illegal") // GET creates the game

            let! first =
                ctx.PostAsync(
                    "/games/e2e-illegal/moves",
                    APIRequestContextOptions(DataObject = {| position = "TopLeft"; player = "X" |})
                )

            Assert.That(first.Status, Is.EqualTo 200)

            // X moves again — not X's turn.
            let! second =
                ctx.PostAsync(
                    "/games/e2e-illegal/moves",
                    APIRequestContextOptions(DataObject = {| position = "TopCenter"; player = "X" |})
                )

            Assert.That(second.Status, Is.EqualTo 409)
        }
