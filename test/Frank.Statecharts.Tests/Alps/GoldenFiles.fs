module internal Frank.Statecharts.Tests.Alps.GoldenFiles

[<Literal>]
let ticTacToeAlpsJson = """{
  "alps": {
    "version": "1.0",
    "doc": { "format": "text", "value": "Tic-Tac-Toe game state machine" },
    "descriptor": [
      {
        "id": "gameState",
        "type": "semantic",
        "doc": { "format": "text", "value": "Current state of the game board" }
      },
      {
        "id": "position",
        "type": "semantic",
        "doc": { "format": "text", "value": "Board position (0-8)" }
      },
      {
        "id": "player",
        "type": "semantic",
        "doc": { "format": "text", "value": "Player identifier (X or O)" }
      },
      {
        "id": "XTurn",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Player X's turn" },
        "descriptor": [
          {
            "id": "makeMove",
            "type": "unsafe",
            "rt": "#OTurn",
            "doc": { "format": "text", "value": "Player X makes a move, transitions to O's turn" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "role=PlayerX" }
            ]
          },
          {
            "id": "makeMove",
            "type": "unsafe",
            "rt": "#Won",
            "doc": { "format": "text", "value": "Player X makes a winning move" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "wins" }
            ]
          },
          {
            "id": "makeMove",
            "type": "unsafe",
            "rt": "#Draw",
            "doc": { "format": "text", "value": "Player X makes a move that fills the board" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "boardFull" }
            ]
          },
          { "href": "#viewGame" }
        ]
      },
      {
        "id": "OTurn",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Player O's turn" },
        "descriptor": [
          {
            "id": "makeMove",
            "type": "unsafe",
            "rt": "#XTurn",
            "doc": { "format": "text", "value": "Player O makes a move, transitions to X's turn" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "role=PlayerO" }
            ]
          },
          {
            "id": "makeMove",
            "type": "unsafe",
            "rt": "#Won",
            "doc": { "format": "text", "value": "Player O makes a winning move" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "wins" }
            ]
          },
          {
            "id": "makeMove",
            "type": "unsafe",
            "rt": "#Draw",
            "doc": { "format": "text", "value": "Player O makes a move that fills the board" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "boardFull" }
            ]
          },
          { "href": "#viewGame" }
        ]
      },
      {
        "id": "Won",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Game won by a player" },
        "descriptor": [
          { "href": "#viewGame" }
        ]
      },
      {
        "id": "Draw",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Game ended in a draw" },
        "descriptor": [
          { "href": "#viewGame" }
        ]
      },
      {
        "id": "viewGame",
        "type": "safe",
        "rt": "#gameState",
        "doc": { "format": "text", "value": "View the current game state" }
      }
    ],
    "link": [
      { "rel": "self", "href": "http://example.com/alps/tic-tac-toe" }
    ]
  }
}"""

[<Literal>]
let ticTacToeAlpsXml = """<?xml version="1.0" encoding="UTF-8"?>
<alps version="1.0">
  <doc format="text">Tic-Tac-Toe game state machine</doc>
  <descriptor id="gameState" type="semantic">
    <doc format="text">Current state of the game board</doc>
  </descriptor>
  <descriptor id="position" type="semantic">
    <doc format="text">Board position (0-8)</doc>
  </descriptor>
  <descriptor id="player" type="semantic">
    <doc format="text">Player identifier (X or O)</doc>
  </descriptor>
  <descriptor id="XTurn" type="semantic">
    <doc format="text">State: Player X's turn</doc>
    <descriptor id="makeMove" type="unsafe" rt="#OTurn">
      <doc format="text">Player X makes a move, transitions to O's turn</doc>
      <descriptor href="#position"/>
      <descriptor href="#player"/>
      <ext id="guard" value="role=PlayerX"/>
    </descriptor>
    <descriptor id="makeMove" type="unsafe" rt="#Won">
      <doc format="text">Player X makes a winning move</doc>
      <descriptor href="#position"/>
      <descriptor href="#player"/>
      <ext id="guard" value="wins"/>
    </descriptor>
    <descriptor id="makeMove" type="unsafe" rt="#Draw">
      <doc format="text">Player X makes a move that fills the board</doc>
      <descriptor href="#position"/>
      <descriptor href="#player"/>
      <ext id="guard" value="boardFull"/>
    </descriptor>
    <descriptor href="#viewGame"/>
  </descriptor>
  <descriptor id="OTurn" type="semantic">
    <doc format="text">State: Player O's turn</doc>
    <descriptor id="makeMove" type="unsafe" rt="#XTurn">
      <doc format="text">Player O makes a move, transitions to X's turn</doc>
      <descriptor href="#position"/>
      <descriptor href="#player"/>
      <ext id="guard" value="role=PlayerO"/>
    </descriptor>
    <descriptor id="makeMove" type="unsafe" rt="#Won">
      <doc format="text">Player O makes a winning move</doc>
      <descriptor href="#position"/>
      <descriptor href="#player"/>
      <ext id="guard" value="wins"/>
    </descriptor>
    <descriptor id="makeMove" type="unsafe" rt="#Draw">
      <doc format="text">Player O makes a move that fills the board</doc>
      <descriptor href="#position"/>
      <descriptor href="#player"/>
      <ext id="guard" value="boardFull"/>
    </descriptor>
    <descriptor href="#viewGame"/>
  </descriptor>
  <descriptor id="Won" type="semantic">
    <doc format="text">State: Game won by a player</doc>
    <descriptor href="#viewGame"/>
  </descriptor>
  <descriptor id="Draw" type="semantic">
    <doc format="text">State: Game ended in a draw</doc>
    <descriptor href="#viewGame"/>
  </descriptor>
  <descriptor id="viewGame" type="safe" rt="#gameState">
    <doc format="text">View the current game state</doc>
  </descriptor>
  <link rel="self" href="http://example.com/alps/tic-tac-toe"/>
</alps>"""

[<Literal>]
let onboardingAlpsJson = """{
  "alps": {
    "version": "1.0",
    "doc": { "format": "text", "value": "User onboarding flow" },
    "descriptor": [
      {
        "id": "email",
        "type": "semantic",
        "doc": { "format": "text", "value": "User email address" }
      },
      {
        "id": "name",
        "type": "semantic",
        "doc": { "format": "text", "value": "User display name" }
      },
      {
        "id": "bio",
        "type": "semantic",
        "doc": { "format": "text", "value": "User biography" }
      },
      {
        "id": "Welcome",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Welcome screen" },
        "descriptor": [
          {
            "id": "start",
            "type": "safe",
            "rt": "#CollectEmail",
            "doc": { "format": "text", "value": "Begin the onboarding process" }
          }
        ]
      },
      {
        "id": "CollectEmail",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Collect user email" },
        "descriptor": [
          {
            "id": "submitEmail",
            "type": "unsafe",
            "rt": "#CollectProfile",
            "doc": { "format": "text", "value": "Submit email and proceed to profile" },
            "descriptor": [
              { "href": "#email" }
            ]
          }
        ]
      },
      {
        "id": "CollectProfile",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Collect user profile" },
        "descriptor": [
          {
            "id": "submitProfile",
            "type": "unsafe",
            "rt": "#Review",
            "doc": { "format": "text", "value": "Submit profile and proceed to review" },
            "descriptor": [
              { "href": "#name" },
              { "href": "#bio" }
            ]
          }
        ]
      },
      {
        "id": "Review",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Review submitted information" },
        "descriptor": [
          {
            "id": "confirmReview",
            "type": "unsafe",
            "rt": "#Complete",
            "doc": { "format": "text", "value": "Confirm and complete onboarding" }
          },
          {
            "id": "editEmail",
            "type": "safe",
            "rt": "#CollectEmail",
            "doc": { "format": "text", "value": "Go back to edit email" }
          },
          {
            "id": "editProfile",
            "type": "safe",
            "rt": "#CollectProfile",
            "doc": { "format": "text", "value": "Go back to edit profile" }
          }
        ]
      },
      {
        "id": "Complete",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Onboarding complete" }
      }
    ]
  }
}"""

/// Amundsen-style onboarding fixture (home/WIP/customerData states).
/// Demonstrates the ALPS profile style from Mike Amundsen's book, featuring:
///   - States: home, wip, customerData
///   - Transitions: startOnboarding (safe), collectCustomerData (unsafe), completeOnboarding (unsafe)
///   - Data descriptors: identifier, name, email
///   - Root-level link and ext elements
///   - Documentation at document, state, and transition levels
///   - Guard extension on collectCustomerData
[<Literal>]
let amundsenOnboardingAlpsJson = """{
  "alps": {
    "version": "1.0",
    "doc": { "format": "text", "value": "Customer onboarding profile (Amundsen style)" },
    "link": [
      { "rel": "self", "href": "http://example.com/alps/customer-onboarding" }
    ],
    "ext": [
      { "id": "author", "value": "amundsen" }
    ],
    "descriptor": [
      {
        "id": "identifier",
        "type": "semantic",
        "doc": { "format": "text", "value": "Unique customer identifier" }
      },
      {
        "id": "name",
        "type": "semantic",
        "doc": { "format": "text", "value": "Customer display name" }
      },
      {
        "id": "email",
        "type": "semantic",
        "doc": { "format": "text", "value": "Customer email address" }
      },
      {
        "id": "home",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Home/entry point for onboarding" },
        "descriptor": [
          {
            "id": "startOnboarding",
            "type": "safe",
            "rt": "#wip",
            "doc": { "format": "text", "value": "Begin the customer onboarding workflow" }
          }
        ]
      },
      {
        "id": "wip",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Work-in-progress, collecting customer data" },
        "descriptor": [
          {
            "id": "collectCustomerData",
            "type": "unsafe",
            "rt": "#customerData",
            "doc": { "format": "text", "value": "Submit customer data fields" },
            "descriptor": [
              { "href": "#name" },
              { "href": "#email" }
            ],
            "ext": [
              { "id": "guard", "value": "emailValid" }
            ]
          },
          {
            "id": "completeOnboarding",
            "type": "unsafe",
            "rt": "#home",
            "doc": { "format": "text", "value": "Complete onboarding and return to home" },
            "descriptor": [
              { "href": "#identifier" }
            ]
          }
        ]
      },
      {
        "id": "customerData",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Customer data collected and confirmed" }
      }
    ]
  }
}"""

[<Literal>]
let onboardingAlpsXml = """<?xml version="1.0" encoding="UTF-8"?>
<alps version="1.0">
  <doc format="text">User onboarding flow</doc>
  <descriptor id="email" type="semantic">
    <doc format="text">User email address</doc>
  </descriptor>
  <descriptor id="name" type="semantic">
    <doc format="text">User display name</doc>
  </descriptor>
  <descriptor id="bio" type="semantic">
    <doc format="text">User biography</doc>
  </descriptor>
  <descriptor id="Welcome" type="semantic">
    <doc format="text">State: Welcome screen</doc>
    <descriptor id="start" type="safe" rt="#CollectEmail">
      <doc format="text">Begin the onboarding process</doc>
    </descriptor>
  </descriptor>
  <descriptor id="CollectEmail" type="semantic">
    <doc format="text">State: Collect user email</doc>
    <descriptor id="submitEmail" type="unsafe" rt="#CollectProfile">
      <doc format="text">Submit email and proceed to profile</doc>
      <descriptor href="#email"/>
    </descriptor>
  </descriptor>
  <descriptor id="CollectProfile" type="semantic">
    <doc format="text">State: Collect user profile</doc>
    <descriptor id="submitProfile" type="unsafe" rt="#Review">
      <doc format="text">Submit profile and proceed to review</doc>
      <descriptor href="#name"/>
      <descriptor href="#bio"/>
    </descriptor>
  </descriptor>
  <descriptor id="Review" type="semantic">
    <doc format="text">State: Review submitted information</doc>
    <descriptor id="confirmReview" type="unsafe" rt="#Complete">
      <doc format="text">Confirm and complete onboarding</doc>
    </descriptor>
    <descriptor id="editEmail" type="safe" rt="#CollectEmail">
      <doc format="text">Go back to edit email</doc>
    </descriptor>
    <descriptor id="editProfile" type="safe" rt="#CollectProfile">
      <doc format="text">Go back to edit profile</doc>
    </descriptor>
  </descriptor>
  <descriptor id="Complete" type="semantic">
    <doc format="text">State: Onboarding complete</doc>
  </descriptor>
</alps>"""
