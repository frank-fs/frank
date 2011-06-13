#!/bin/bash

platform='x86'
monopath='/usr/lib/mono/4.0/'
fscorepath='/usr/local/lib/mono/4.0/'
franklibpath='lib/wcf/'

`fsc -o:Frank.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$monopath'mscorlib.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank/Frack.fs src/lib/Frank/Middleware.fs > /dev/null`

`fsc -o:Frank.SystemWeb.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$monopath'mscorlib.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.dll' -r:$monopath'System.Web.dll' -r:$monopath'System.Web.Abstractions.dll' -r:'Frank.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank.SystemWeb/Frank.SystemWeb.fs > /dev/null`

`fsc -o:Frank.HttpListener.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$monopath'mscorlib.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.dll' -r:$monopath'System.Web.dll' -r:$monopath'System.Web.Abstractions.dll' -r:'Frank.SystemWeb.dll' -r:'Frank.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank.HttpListener/Frank.HttpListener.fs > /dev/null`

`fsc -o:Frank.Wcf.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$franklibpath'Microsoft.Net.Http.dll' -r:$franklibpath'Microsoft.QueryComposition.dll' -r:$franklibpath'Microsoft.Runtime.Serialization.Json.dll' -r:$franklibpath'Microsoft.ServiceModel.Http.dll' -r:$franklibpath'Microsoft.ServiceModel.Web.jQuery.dll' -r:$franklibpath'Microsoft.ServiceModel.WebHttp.dll' -r:$monopath'System.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.ServiceModel.dll' -r:$monopath'System.ServiceModel.Web.dll' -r:'Frank.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank.Wcf/Frank.Wcf.fs > /dev/null`

`fsc -o:Frank.AspNet.dll --debug:pdbonly --noframework --optimize+ --define:BIGINTEGER --platform:$platform -r:$fscorepath'FSharp.Core.dll' -r:$monopath'System.dll' -r:$monopath'System.Core.dll' -r:$monopath'System.Web.dll' -r:$monopath'System.Web.Abstractions.dll' -r:$monopath'System.Web.Routing.dll' -r:'Frank.SystemWeb.dll' -r:'Frank.dll' --target:library --warn:4 --warnaserror:76 --vserrors --LCID:1033 --utf8output --fullpaths --flaterrors src/lib/Frank.AspNet/Frank.AspNet.fs > /dev/null`
