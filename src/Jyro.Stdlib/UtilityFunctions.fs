namespace Mesch.Jyro

open System
open System.Text.Json
open System.Threading.Tasks

/// Utility functions
module UtilityFunctions =

    type TypeOfFunction() =
        inherit JyroFunctionBase("TypeOf", FunctionSignatures.unary "TypeOf" AnyParam StringParam)
        override _.ExecuteImpl(args, _) =
            let value = args.[0]
            let typeName =
                match value.ValueType with
                | JyroValueType.Null -> "null"
                | JyroValueType.Boolean -> "boolean"
                | JyroValueType.Number -> "number"
                | JyroValueType.String -> "string"
                | JyroValueType.Array -> "array"
                | JyroValueType.Object -> "object"
                | _ -> "unknown"
            JyroString(typeName) :> JyroValue

    type CoalesceFunction() =
        inherit JyroFunctionBase("Coalesce", FunctionSignatures.unary "Coalesce" ArrayParam AnyParam)
        override this.ExecuteImpl(args, _) =
            let input = this.GetArrayArgument(args, 0)
            let mutable result: JyroValue = JyroNull.Instance
            let mutable i = 0
            while i < input.Length && result.IsNull do
                if not input.Items.[i].IsNull then
                    result <- input.Items.[i]
                i <- i + 1
            result

    type ToStringFunction() =
        inherit JyroFunctionBase("ToString", FunctionSignatures.unary "ToString" AnyParam StringParam)
        override _.ExecuteImpl(args, _) =
            JyroString(args.[0].ToStringValue()) :> JyroValue

    type ToBooleanFunction() =
        inherit JyroFunctionBase("ToBoolean", FunctionSignatures.unary "ToBoolean" AnyParam BooleanParam)
        override _.ExecuteImpl(args, _) =
            JyroBoolean.FromBoolean(args.[0].ToBooleanTruthiness()) :> JyroValue

    type KeysFunction() =
        inherit JyroFunctionBase("Keys", FunctionSignatures.unary "Keys" ObjectParam ArrayParam)
        override this.ExecuteImpl(args, _) =
            let obj = this.GetObjectArgument(args, 0)
            let result = JyroArray()
            for key in obj.Properties.Keys do
                result.Add(JyroString(key))
            result :> JyroValue

    type ValuesFunction() =
        inherit JyroFunctionBase("Values", FunctionSignatures.unary "Values" ObjectParam ArrayParam)
        override this.ExecuteImpl(args, _) =
            let obj = this.GetObjectArgument(args, 0)
            let result = JyroArray()
            for value in obj.Properties.Values do
                result.Add(value)
            result :> JyroValue

    type HasPropertyFunction() =
        inherit JyroFunctionBase("HasProperty", FunctionSignatures.binary "HasProperty" ObjectParam StringParam BooleanParam)
        override this.ExecuteImpl(args, _) =
            let obj = this.GetObjectArgument(args, 0)
            let key = this.GetStringArgument(args, 1)
            JyroBoolean.FromBoolean(obj.Properties.ContainsKey(key)) :> JyroValue

    type MergeFunction() =
        inherit JyroFunctionBase("Merge", FunctionSignatures.unary "Merge" ArrayParam ObjectParam)
        override this.ExecuteImpl(args, _) =
            let input = this.GetArrayArgument(args, 0)
            let result = JyroObject()
            for item in input.Items do
                match item with
                | :? JyroObject as obj ->
                    for kvp in obj.Properties do
                        result.SetProperty(kvp.Key, kvp.Value)
                | _ -> ()
            result :> JyroValue

    type CloneFunction() =
        inherit JyroFunctionBase("Clone", FunctionSignatures.unary "Clone" AnyParam AnyParam)
        override _.ExecuteImpl(args, _) =
            let rec clone (value: JyroValue) : JyroValue =
                match value with
                | :? JyroNull -> JyroNull.Instance :> JyroValue
                | :? JyroBoolean as b -> JyroBoolean.FromBoolean(b.Value) :> JyroValue
                | :? JyroNumber as n -> JyroNumber(n.Value) :> JyroValue
                | :? JyroString as s -> JyroString(s.Value) :> JyroValue
                | :? JyroArray as arr ->
                    let result = JyroArray()
                    for item in arr.Items do
                        result.Add(clone item)
                    result :> JyroValue
                | :? JyroObject as obj ->
                    let result = JyroObject()
                    for kvp in obj.Properties do
                        result.SetProperty(kvp.Key, clone kvp.Value)
                    result :> JyroValue
                | _ -> value
            clone args.[0]

    type NewGuidFunction() =
        inherit JyroFunctionBase("NewGuid",
            FunctionSignatures.create "NewGuid" [] StringParam)
        override _.ExecuteImpl(_, _) =
            let guid = Guid.NewGuid()
            JyroString(guid.ToString("D").ToLowerInvariant()) :> JyroValue

    type Base64EncodeFunction() =
        inherit JyroFunctionBase("Base64Encode", FunctionSignatures.unary "Base64Encode" StringParam StringParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            let bytes = Text.Encoding.UTF8.GetBytes(str)
            JyroString(Convert.ToBase64String(bytes)) :> JyroValue

    type Base64DecodeFunction() =
        inherit JyroFunctionBase("Base64Decode", FunctionSignatures.unary "Base64Decode" StringParam StringParam)
        override this.ExecuteImpl(args, _) =
            let str = this.GetStringArgument(args, 0)
            try
                let bytes = Convert.FromBase64String(str)
                JyroString(Text.Encoding.UTF8.GetString(bytes)) :> JyroValue
            with :? FormatException as ex ->
                JyroError.raiseRuntime MessageCode.Base64DecodeError [| box ex.Message |]

    type FromJsonFunction() =
        inherit JyroFunctionBase("FromJson", FunctionSignatures.unary "FromJson" StringParam AnyParam)
        override this.ExecuteImpl(args, _) =
            let jsonStr = this.GetStringArgument(args, 0)
            try
                let doc = JsonDocument.Parse(jsonStr)
                let rec convert (el: JsonElement) : JyroValue =
                    match el.ValueKind with
                    | JsonValueKind.Null -> JyroNull.Instance :> JyroValue
                    | JsonValueKind.True -> JyroBoolean.True :> JyroValue
                    | JsonValueKind.False -> JyroBoolean.False :> JyroValue
                    | JsonValueKind.Number -> JyroNumber(el.GetDouble()) :> JyroValue
                    | JsonValueKind.String -> JyroString(el.GetString()) :> JyroValue
                    | JsonValueKind.Array ->
                        let arr = JyroArray()
                        for item in el.EnumerateArray() do
                            arr.Add(convert item)
                        arr :> JyroValue
                    | JsonValueKind.Object ->
                        let obj = JyroObject()
                        for prop in el.EnumerateObject() do
                            obj.SetProperty(prop.Name, convert prop.Value)
                        obj :> JyroValue
                    | _ -> JyroNull.Instance :> JyroValue
                convert doc.RootElement
            with
            | _ -> JyroNull.Instance :> JyroValue

    type ToJsonFunction() =
        inherit JyroFunctionBase("ToJson", FunctionSignatures.unary "ToJson" AnyParam StringParam)
        override _.ExecuteImpl(args, _) =
            JyroString(args.[0].ToJson()) :> JyroValue

    type SleepFunction() =
        inherit JyroFunctionBase("Sleep",
            FunctionSignatures.create "Sleep"
                [ Parameter.Required("ms", NumberParam) ]
                NullParam)
        override this.ExecuteImpl(args, ctx) =
            let msArg = this.GetArgument<JyroNumber>(args, 0)
            if not msArg.IsInteger || msArg.Value < 0.0 then
                JyroError.raiseRuntime MessageCode.NonNegativeIntegerRequired
                    [| box "Sleep()"; box "a non-negative integer millisecond value"; box msArg.Value |]
            // No-op in WASM — Task.Delay().GetResult() deadlocks on the single-threaded runtime
            // because the timer callback can never fire while the thread is blocked.
            if not (OperatingSystem.IsBrowser()) then
                let ms = msArg.ToInteger()
                Task.Delay(ms, ctx.CancellationToken).GetAwaiter().GetResult()
            JyroNull.Instance :> JyroValue

    type DiffFunction() =
        inherit JyroFunctionBase("Diff", FunctionSignatures.binary "Diff" ObjectParam ObjectParam ObjectParam)
        override this.ExecuteImpl(args, _) =
            let obj1 = this.GetObjectArgument(args, 0)
            let obj2 = this.GetObjectArgument(args, 1)

            let added = JyroObject()
            let removed = JyroObject()
            let changed = JyroObject()

            // Find removed and changed keys (iterate obj1)
            for kvp in obj1.Properties do
                match obj2.Properties.TryGetValue(kvp.Key) with
                | true, v2 ->
                    // Both null => no change; otherwise use deep equality
                    if not (kvp.Value.IsNull && v2.IsNull) && not (kvp.Value.EqualsValue(v2)) then
                        let pair = JyroObject()
                        pair.SetProperty("from", kvp.Value)
                        pair.SetProperty("to", v2)
                        changed.SetProperty(kvp.Key, pair)
                | false, _ ->
                    removed.SetProperty(kvp.Key, kvp.Value)

            // Find added keys (iterate obj2, skip those already in obj1)
            for kvp in obj2.Properties do
                if not (obj1.Properties.ContainsKey(kvp.Key)) then
                    added.SetProperty(kvp.Key, kvp.Value)

            let result = JyroObject()
            result.SetProperty("added", added)
            result.SetProperty("removed", removed)
            result.SetProperty("changed", changed)
            result :> JyroValue

    /// Get all utility functions
    let getAll () : IJyroFunction list =
        [ TypeOfFunction()
          CoalesceFunction()
          ToStringFunction()
          ToBooleanFunction()
          KeysFunction()
          ValuesFunction()
          HasPropertyFunction()
          MergeFunction()
          CloneFunction()
          NewGuidFunction()
          Base64EncodeFunction()
          Base64DecodeFunction()
          FromJsonFunction()
          ToJsonFunction()
          SleepFunction()
          DiffFunction() ]
