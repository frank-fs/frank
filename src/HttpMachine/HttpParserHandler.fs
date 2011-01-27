namespace HttpMachine
open System

type HttpRequestMessageParseEvent =
  | RequestMessageBegin
  | RequestMethod of seq<byte>
  | RequestUri of seq<byte>
  | QueryString of seq<byte>
  | Fragment of seq<byte>
  | RequestHeaderName of seq<byte>
  | RequestHeaderValue of seq<byte>
  | RequestHeadersEnd
  | RequestBody of seq<byte>
  | RequestMessageEnd

type HttpResponseMessageParseEvent =
  | ResponseMessageBegin
  | StatusCode of seq<byte>
  | StatusDescription of seq<byte>
  | ResponseHeaderName of seq<byte>
  | ResponseHeaderValue of seq<byte>
  | ResponseHeadersEnd
  | ResponseBody of seq<byte>
  | ResponseMessageEnd
