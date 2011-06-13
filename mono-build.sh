#!/bin/bash

platform='x86'
monopath='/usr/lib/mono/4.0/'
fscorepath='/usr/local/lib/mono/4.0/'

`fsc -o:Frank.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$monopath'mscorlib.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank/Frack.fs src/lib/Frank/Middleware.fs > /dev/null`

`fsc -o:Frank.SystemWeb.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$monopath'mscorlib.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.dll' -r:$monopath'System.Web.dll' -r:$monopath'System.Web.Abstractions.dll' -r:'Frank.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank.SystemWeb/Frank.SystemWeb.fs > /dev/null`

`fsc -o:Frank.HttpListener.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$monopath'mscorlib.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.dll' -r:$monopath'System.Web.dll' -r:$monopath'System.Web.Abstractions.dll' -r:'Frank.SystemWeb.dll' -r:'Frank.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank.HttpListener/Frank.HttpListener.fs > /dev/null`
