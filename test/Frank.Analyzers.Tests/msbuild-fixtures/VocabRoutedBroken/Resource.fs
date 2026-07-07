module VocabRoutedBrokenFixture

// resource "/tic" CE — route /tic does NOT cover /tictactoe# namespace path
// (routeCoversNsPath "/tic" "/tictactoe" = false: "/tictactoe" does not start with "/tic/")
let tttResource =
    resource "/tic" {
        ignore ()
    }
