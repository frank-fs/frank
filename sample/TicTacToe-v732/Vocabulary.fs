module TicTacToe.Vocabulary

open Frank.Semantic
open TicTacToe.Model

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "wikidata" "https://www.wikidata.org/wiki/"
        using "schema"
        seeAlso typeof<Game> "wikidata:Q11907"
        equivalentClass typedefof<MoveLog<_>> "schema:ItemList"
    }
