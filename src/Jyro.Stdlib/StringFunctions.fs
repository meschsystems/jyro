namespace Mesch.Jyro

open System
open System.Globalization
open System.Security.Cryptography
open System.Text.RegularExpressions

/// String manipulation functions
module StringFunctions =

    type ToUpperFunction() =
        inherit JyroFunctionBase("ToUpper", FunctionSignatures.unary "ToUpper" StringParam StringParam)
        override this.ExecuteImpl(args, _) =
            JyroString(this.GetStringArgument(args, 0).ToUpperInvariant()) :> JyroValue

    type ToLowerFunction() =
        inherit JyroFunctionBase("ToLower", FunctionSignatures.unary "ToLower" StringParam StringParam)
        override this.ExecuteImpl(args, _) =
            JyroString(this.GetStringArgument(args, 0).ToLowerInvariant()) :> JyroValue

    type TrimFunction() =
        inherit JyroFunctionBase("Trim", FunctionSignatures.unary "Trim" StringParam StringParam)
        override this.ExecuteImpl(args, _) =
            JyroString(this.GetStringArgument(args, 0).Trim()) :> JyroValue

    type ReplaceFunction() =
        inherit JyroFunctionBase("Replace",
            FunctionSignatures.create "Replace"
                [ Parameter.Required("source", StringParam)
                  Parameter.Required("oldValue", StringParam)
                  Parameter.Required("newValue", StringParam) ]
                StringParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let oldValue = this.GetStringArgument(args, 1)
            let newValue = this.GetStringArgument(args, 2)
            JyroString(str.Replace(oldValue, newValue)) :> JyroValue

    type ContainsFunction() =
        inherit JyroFunctionBase("Contains",
            FunctionSignatures.create "Contains"
                [ Parameter.Required("text", AnyParam)
                  Parameter.Required("search", AnyParam) ]
                BooleanParam)
        override _.ExecuteImpl(args, _) =
            let source = args.[0]
            let search = args.[1]
            if source.IsNull || search.IsNull then
                JyroBoolean.False :> JyroValue
            else
                match source, search with
                | :? JyroString as sourceStr, (:? JyroString as searchStr) ->
                    JyroBoolean.FromBoolean(sourceStr.Value.Contains(searchStr.Value)) :> JyroValue
                | :? JyroArray as arr, _ ->
                    let mutable found = false
                    for item in arr.Items do
                        if not found && item.EqualsValue(search) then
                            found <- true
                    JyroBoolean.FromBoolean(found) :> JyroValue
                | _ ->
                    JyroError.raiseRuntime MessageCode.StringOrArrayRequired [| box "Contains()" |]

    type StartsWithFunction() =
        inherit JyroFunctionBase("StartsWith", FunctionSignatures.binary "StartsWith" StringParam StringParam BooleanParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let prefix = this.GetStringArgument(args, 1)
            JyroBoolean.FromBoolean(str.StartsWith(prefix)) :> JyroValue

    type EndsWithFunction() =
        inherit JyroFunctionBase("EndsWith", FunctionSignatures.binary "EndsWith" StringParam StringParam BooleanParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let suffix = this.GetStringArgument(args, 1)
            JyroBoolean.FromBoolean(str.EndsWith(suffix)) :> JyroValue

    type SplitFunction() =
        inherit JyroFunctionBase("Split", FunctionSignatures.binary "Split" StringParam StringParam ArrayParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let delimiter = this.GetStringArgument(args, 1)
            let parts = str.Split([| delimiter |], StringSplitOptions.None)
            let arr = JyroArray()
            for part in parts do
                arr.Add(JyroString(part))
            arr :> JyroValue

    type JoinFunction() =
        inherit JyroFunctionBase("Join", FunctionSignatures.binary "Join" ArrayParam StringParam StringParam)
        override this.ExecuteImpl(args, _) =
            let arr = this.GetArrayArgument(args, 0)
            let separator = this.GetStringArgument(args, 1)
            let parts = Collections.Generic.List<string>()
            for item in arr.Items do
                match item with
                | :? JyroString as s -> parts.Add(s.Value)
                | v when v.IsNull -> parts.Add("null")
                | v ->
                    match v.ToString() with
                    | null -> parts.Add("")
                    | s -> parts.Add(s)
            JyroString(String.Join(separator, parts)) :> JyroValue

    type SubstringFunction() =
        inherit JyroFunctionBase("Substring",
            FunctionSignatures.create "Substring"
                [ Parameter.Required("text", StringParam)
                  Parameter.Required("start", NumberParam)
                  Parameter.Optional("length", NumberParam) ]
                StringParam)
        override this.ExecuteImpl(args, _) =
            let text = this.GetStringArgument(args, 0)
            let startArg = this.GetArgument<JyroNumber>(args, 1)
            let mutable start = startArg.ToInteger()

            // Handle negative start index
            if start < 0 then start <- 0

            // Handle start beyond string length
            if start >= text.Length then
                JyroString(String.Empty) :> JyroValue
            else
                let length =
                    if args.Count > 2 then
                        let lengthArg = this.GetArgument<JyroNumber>(args, 2)
                        let len = lengthArg.ToInteger()
                        if len <= 0 then 0
                        else Math.Min(len, text.Length - start)
                    else
                        text.Length - start
                if length = 0 then
                    JyroString(String.Empty) :> JyroValue
                else
                    JyroString(text.Substring(start, length)) :> JyroValue

    type ToNumberFunction() =
        inherit JyroFunctionBase("ToNumber", FunctionSignatures.unary "ToNumber" StringParam AnyParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            match Double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture) with
            | true, n -> JyroNumber(n) :> JyroValue
            | _ -> JyroNull.Instance :> JyroValue

    let private maxPadWidth = 1_000_000

    type PadLeftFunction() =
        inherit JyroFunctionBase("PadLeft",
            FunctionSignatures.create "PadLeft"
                [ Parameter.Required("text", StringParam)
                  Parameter.Required("length", NumberParam)
                  Parameter.Optional("padChar", StringParam) ]
                StringParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let totalWidth = int (this.GetNumberArgument(args, 1))
            if totalWidth > maxPadWidth then
                JyroError.raiseRuntime MessageCode.PadLengthExceeded [| box "PadLeft()"; box totalWidth; box maxPadWidth |]
            let padChar =
                if args.Count > 2 then
                    let s = this.GetStringArgument(args, 2)
                    if String.IsNullOrEmpty(s) then ' ' else s.[0]
                else ' '
            JyroString(str.PadLeft(totalWidth, padChar)) :> JyroValue

    type PadRightFunction() =
        inherit JyroFunctionBase("PadRight",
            FunctionSignatures.create "PadRight"
                [ Parameter.Required("text", StringParam)
                  Parameter.Required("length", NumberParam)
                  Parameter.Optional("padChar", StringParam) ]
                StringParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let totalWidth = int (this.GetNumberArgument(args, 1))
            if totalWidth > maxPadWidth then
                JyroError.raiseRuntime MessageCode.PadLengthExceeded [| box "PadRight()"; box totalWidth; box maxPadWidth |]
            let padChar =
                if args.Count > 2 then
                    let s = this.GetStringArgument(args, 2)
                    if String.IsNullOrEmpty(s) then ' ' else s.[0]
                else ' '
            JyroString(str.PadRight(totalWidth, padChar)) :> JyroValue

    let private regexTimeout = TimeSpan.FromSeconds(1.0)

    type RegexTestFunction() =
        inherit JyroFunctionBase("RegexTest", FunctionSignatures.binary "RegexTest" StringParam StringParam BooleanParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let pattern = this.GetStringArgument(args, 1)
            try
                let regex = Regex(pattern, RegexOptions.None, regexTimeout)
                JyroBoolean.FromBoolean(regex.IsMatch(str)) :> JyroValue
            with
            | :? RegexMatchTimeoutException ->
                JyroError.raiseRuntime MessageCode.RegexTimeout [| box "RegexTest()" |]
            | :? ArgumentException as ex ->
                JyroError.raiseRuntime MessageCode.RegexInvalidPattern [| box "RegexTest()"; box ex.Message |]

    type RegexMatchFunction() =
        inherit JyroFunctionBase("RegexMatch", FunctionSignatures.binary "RegexMatch" StringParam StringParam StringParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let pattern = this.GetStringArgument(args, 1)
            try
                let regex = Regex(pattern, RegexOptions.None, regexTimeout)
                let m = regex.Match(str)
                if m.Success then JyroString(m.Value) :> JyroValue
                else JyroNull.Instance :> JyroValue
            with
            | :? RegexMatchTimeoutException ->
                JyroError.raiseRuntime MessageCode.RegexTimeout [| box "RegexMatch()" |]
            | :? ArgumentException as ex ->
                JyroError.raiseRuntime MessageCode.RegexInvalidPattern [| box "RegexMatch()"; box ex.Message |]

    type RegexMatchAllFunction() =
        inherit JyroFunctionBase("RegexMatchAll", FunctionSignatures.binary "RegexMatchAll" StringParam StringParam ArrayParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let pattern = this.GetStringArgument(args, 1)
            try
                let regex = Regex(pattern, RegexOptions.None, regexTimeout)
                let matches = regex.Matches(str)
                let arr = JyroArray()
                for m in matches do
                    arr.Add(JyroString(m.Value))
                arr :> JyroValue
            with
            | :? RegexMatchTimeoutException ->
                JyroError.raiseRuntime MessageCode.RegexTimeout [| box "RegexMatchAll()" |]
            | :? ArgumentException as ex ->
                JyroError.raiseRuntime MessageCode.RegexInvalidPattern [| box "RegexMatchAll()"; box ex.Message |]

    type RegexMatchDetailFunction() =
        inherit JyroFunctionBase("RegexMatchDetail",
            FunctionSignatures.binary "RegexMatchDetail" StringParam StringParam ObjectParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let pattern = this.GetStringArgument(args, 1)
            try
                let regex = Regex(pattern, RegexOptions.None, regexTimeout)
                let m = regex.Match(str)
                if not m.Success then
                    JyroNull.Instance :> JyroValue
                else
                    let result = JyroObject()
                    result.SetProperty("match", JyroString(m.Value))
                    result.SetProperty("index", JyroNumber(float m.Index))
                    let groups = JyroArray()
                    for i = 1 to m.Groups.Count - 1 do
                        groups.Add(JyroString(m.Groups.[i].Value))
                    result.SetProperty("groups", groups)
                    result :> JyroValue
            with
            | :? RegexMatchTimeoutException ->
                JyroError.raiseRuntime MessageCode.RegexTimeout [| box "RegexMatchDetail()" |]
            | :? ArgumentException as ex ->
                JyroError.raiseRuntime MessageCode.RegexInvalidPattern [| box "RegexMatchDetail()"; box ex.Message |]

    [<Literal>]
    let private DefaultCharset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"

    type RandomStringFunction() =
        inherit JyroFunctionBase("RandomString",
            FunctionSignatures.create "RandomString"
                [ Parameter.Required("length", NumberParam)
                  Parameter.Optional("characterSet", StringParam) ]
                StringParam)
        override this.ExecuteImpl(args, _) =
            let lengthArg = this.GetArgument<JyroNumber>(args, 0)
            if not lengthArg.IsInteger || lengthArg.Value < 0.0 then
                JyroError.raiseRuntime MessageCode.NonNegativeIntegerRequired [| box "RandomString()"; box "a non-negative integer length"; box lengthArg.Value |]
            let length = lengthArg.ToInteger()
            let charset =
                if args.Count > 1 then
                    let s = this.GetStringArgument(args, 1)
                    if String.IsNullOrEmpty(s) then
                        JyroError.raiseRuntime MessageCode.EmptyCharacterSet [| box "RandomString()" |]
                    s
                else DefaultCharset
            let chars = Array.init length (fun _ -> charset.[RandomNumberGenerator.GetInt32(0, charset.Length)])
            JyroString(new string(chars)) :> JyroValue

    /// Get all string functions
    let getAll () : IJyroFunction list =
        [ ToUpperFunction()
          ToLowerFunction()
          TrimFunction()
          ReplaceFunction()
          ContainsFunction()
          StartsWithFunction()
          EndsWithFunction()
          SplitFunction()
          JoinFunction()
          SubstringFunction()
          ToNumberFunction()
          PadLeftFunction()
          PadRightFunction()
          RegexTestFunction()
          RegexMatchFunction()
          RegexMatchAllFunction()
          RegexMatchDetailFunction()
          RandomStringFunction() ]
