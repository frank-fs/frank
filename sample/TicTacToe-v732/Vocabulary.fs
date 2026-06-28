module TicTacToe.Vocabulary

open Frank.Semantic
open TicTacToe.Model

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "wikidata" "http://www.wikidata.org/entity/"
        using "schema"
        seeAlso typeof<Game> "wikidata:Q210339"
        seeAlso typeof<Game> "wikidata:Q573573"
        seeAlso typeof<Game> "wikidata:Q573520"
        equivalentClass typedefof<MoveLog<_>> "schema:ItemList"
        provClass typeof<Move> Activity
    }
