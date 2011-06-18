#!/bin/sh
# Builds the Fracture library.
set -e
"./packages/FAKE.1.56.7/tools/FAKE.exe" "build.fsx"
