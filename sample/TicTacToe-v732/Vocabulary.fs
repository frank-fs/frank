module TicTacToe.Vocabulary

open Frank.Semantic
open TicTacToe.Model

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "wikidata" "http://www.wikidata.org/entity/"
        prefix "ttt" "https://example.org/tictactoe#"
        using "schema"
        seeAlso typeof<Game> "wikidata:Q210339"
        seeAlso typeof<Game> "wikidata:Q573573"
        seeAlso typeof<Game> "wikidata:Q573520"
        equivalentClass typedefof<MoveLog<_>> "schema:ItemList"
        provClass typeof<MoveRequest> Activity

        constrainPattern
            typeof<MoveRequest>
            "Position"
            @"^(TopLeft|TopCenter|TopRight|MiddleLeft|MiddleCenter|MiddleRight|BottomLeft|BottomCenter|BottomRight)$"
    }
