namespace Mesch.Jyro

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text.Json

/// The types of values that can exist in the Jyro runtime
type JyroValueType =
    | Null = 0
    | Boolean = 1
    | Number = 2
    | String = 3
    | Array = 4
    | Object = 5
    | Function = 6

/// Base class for all Jyro runtime values
[<AbstractClass; AllowNullLiteral>]
type JyroValue() =
    /// Gets the specific runtime type of this value
    abstract member ValueType: JyroValueType

    /// Gets whether this represents a null value
    abstract member IsNull: bool
    default _.IsNull = false

    // Property and index access
    abstract member GetProperty: key: string -> JyroValue
    default _.GetProperty(_) = JyroNull.Instance

    abstract member SetProperty: key: string * value: JyroValue -> unit
    default this.SetProperty(key, _) =
        JyroError.raiseRuntime MessageCode.SetPropertyOnNonObject [| box key; box this.ValueType |]

    abstract member RemoveProperty: key: string -> bool
    default this.RemoveProperty(key) =
        JyroError.raiseRuntime MessageCode.SetPropertyOnNonObject [| box key; box this.ValueType |]

    abstract member GetIndex: index: JyroValue -> JyroValue
    default _.GetIndex(_) = JyroNull.Instance

    abstract member SetIndex: index: JyroValue * value: JyroValue -> unit
    default this.SetIndex(_, _) =
        JyroError.raiseRuntime MessageCode.SetIndexOnNonContainer [| box this.ValueType |]

    /// Converts to an enumerable for foreach loops
    abstract member ToIterable: unit -> JyroValue seq
    default this.ToIterable() =
        JyroError.raiseRuntime MessageCode.NotIterable [| box this.ValueType |]

    // Binary operations
    abstract member EvaluateBinary: op: BinaryOp * right: JyroValue -> JyroValue
    default this.EvaluateBinary(op, right) =
        match op with
        | Equal -> JyroBoolean.FromBoolean(this.EqualsValue(right))
        | NotEqual -> JyroBoolean.FromBoolean(not (this.EqualsValue(right)))
        | And -> if not (this.ToBooleanTruthiness()) then this else right
        | Or -> if this.ToBooleanTruthiness() then this else right
        | Coalesce -> if this.IsNull then right else this
        | _ -> raise (JyroRuntimeException(MessageCode.UnsupportedBinaryOperation,
                sprintf "Unsupported binary operation %A for %A" op this.ValueType,
                [| box op; box this.ValueType |]))

    // Unary operations
    abstract member EvaluateUnary: op: UnaryOp -> JyroValue
    default this.EvaluateUnary(op) =
        match op with
        | Not -> JyroBoolean.FromBoolean(not (this.ToBooleanTruthiness()))
        | _ -> raise (JyroRuntimeException(MessageCode.UnsupportedUnaryOperation,
                sprintf "Unsupported unary operation %A for %A" op this.ValueType,
                [| box op; box this.ValueType |]))

    // Type conversions
    abstract member AsObject: unit -> JyroObject
    default this.AsObject() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to Object" this.ValueType, [| box this.ValueType; box "Object" |]))

    abstract member AsArray: unit -> JyroArray
    default this.AsArray() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to Array" this.ValueType, [| box this.ValueType; box "Array" |]))

    abstract member AsString: unit -> JyroString
    default this.AsString() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to String" this.ValueType, [| box this.ValueType; box "String" |]))

    abstract member AsNumber: unit -> JyroNumber
    default this.AsNumber() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to Number" this.ValueType, [| box this.ValueType; box "Number" |]))

    abstract member AsBoolean: unit -> JyroBoolean
    default this.AsBoolean() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to Boolean" this.ValueType, [| box this.ValueType; box "Boolean" |]))

    // Host interoperability
    abstract member ToObjectValue: unit -> obj
    abstract member ToStringValue: unit -> string
    default this.ToStringValue() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to string" this.ValueType, [| box this.ValueType; box "string" |]))

    abstract member ToInt32: unit -> int
    default this.ToInt32() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to int" this.ValueType, [| box this.ValueType; box "int" |]))

    abstract member ToInt64: unit -> int64
    default this.ToInt64() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to long" this.ValueType, [| box this.ValueType; box "long" |]))

    abstract member ToDouble: unit -> float
    default this.ToDouble() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to double" this.ValueType, [| box this.ValueType; box "double" |]))

    abstract member ToBoolean: unit -> bool
    default this.ToBoolean() = raise (JyroRuntimeException(MessageCode.InvalidCast, sprintf "Cannot cast %A to bool" this.ValueType, [| box this.ValueType; box "bool" |]))

    /// Evaluates the truthiness for conditionals
    /// Arrays and objects are always truthy (even when empty), like JavaScript
    member this.ToBooleanTruthiness() : bool =
        match this with
        | :? JyroNull -> false
        | :? JyroBoolean as b -> b.Value
        | :? JyroNumber as n -> n.Value <> 0.0
        | :? JyroString as s -> not (String.IsNullOrEmpty(s.Value))
        | :? JyroArray -> true
        | :? JyroObject -> true
        | :? JyroFunction -> true
        | _ -> false

    /// Equality comparison
    abstract member EqualsValue: other: JyroValue -> bool

    abstract member WriteJson: Utf8JsonWriter -> unit
    default _.WriteJson(writer) = writer.WriteNullValue()

    member this.ToJson() =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        this.WriteJson(writer)
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    static member FromObject(value: obj) : JyroValue =
        match value with
        | null -> JyroNull.Instance :> JyroValue
        | :? JyroValue as jv -> jv
        | :? bool as b -> JyroBoolean.FromBoolean(b) :> JyroValue
        | :? int as i -> JyroNumber(float i) :> JyroValue
        | :? int64 as l -> JyroNumber(float l) :> JyroValue
        | :? float as f -> JyroNumber(f) :> JyroValue
        | :? decimal as d -> JyroNumber(float d) :> JyroValue
        | :? string as s -> JyroString(s) :> JyroValue
        | :? JsonElement as je -> JyroValue.FromJsonElement(je)
        | :? IDictionary<string, obj> as dict -> JyroObject.FromDictionary(dict) :> JyroValue
        | :? IEnumerable<obj> as seq -> JyroArray.FromEnumerable(seq) :> JyroValue
        | :? System.Collections.IEnumerable as seq ->
            let items = ResizeArray<obj>()
            for item in seq do items.Add(item)
            JyroArray.FromEnumerable(items) :> JyroValue
        | other -> JyroString(other.ToString()) :> JyroValue

    static member FromJson(json: string, ?options: JsonSerializerOptions) : JyroValue =
        use doc = JsonDocument.Parse(json)
        JyroValue.FromJsonElement(doc.RootElement)

    static member private FromJsonElement(element: JsonElement) : JyroValue =
        match element.ValueKind with
        | JsonValueKind.Null | JsonValueKind.Undefined -> JyroNull.Instance :> JyroValue
        | JsonValueKind.True -> JyroBoolean.True :> JyroValue
        | JsonValueKind.False -> JyroBoolean.False :> JyroValue
        | JsonValueKind.Number ->
            let rawText = element.GetRawText()
            let isJsonFloat = rawText.Contains('.')
            match element.TryGetDouble() with
            | true, d ->
                let n = JyroNumber(d)
                if isJsonFloat then n.ForceFloat <- true
                n :> JyroValue
            | _ -> JyroNumber(0.0) :> JyroValue
        | JsonValueKind.String -> JyroString(element.GetString() |> Option.ofObj |> Option.defaultValue "") :> JyroValue
        | JsonValueKind.Array ->
            let arr = JyroArray()
            for item in element.EnumerateArray() do
                arr.Add(JyroValue.FromJsonElement(item))
            arr :> JyroValue
        | JsonValueKind.Object ->
            let obj = JyroObject()
            for prop in element.EnumerateObject() do
                obj.SetProperty(prop.Name, JyroValue.FromJsonElement(prop.Value))
            obj :> JyroValue
        | _ -> JyroNull.Instance :> JyroValue

    /// Coerces a value to the target type for type hint enforcement.
    /// Returns the coerced value if conversion succeeds, or throws JyroRuntimeException.
    static member CoerceToType(value: JyroValue, targetType: JyroValueType, variableName: string) : JyroValue =
        // If already the right type, pass through
        if value.ValueType = targetType then value
        else
            match targetType with
            | JyroValueType.Number ->
                match value with
                | :? JyroString as s ->
                    match Double.TryParse(s.Value, NumberStyles.Any, CultureInfo.InvariantCulture) with
                    | true, n -> JyroNumber(n) :> JyroValue
                    | _ -> raise (JyroRuntimeException(MessageCode.InvalidType,
                            sprintf "Cannot assign string to variable '%s' of type number" variableName))
                | :? JyroBoolean as b ->
                    JyroNumber(if b.Value then 1.0 else 0.0) :> JyroValue
                | _ -> raise (JyroRuntimeException(MessageCode.InvalidType,
                        sprintf "Cannot assign %A to variable '%s' of type number" value.ValueType variableName))
            | JyroValueType.String ->
                match value with
                | :? JyroNumber | :? JyroBoolean ->
                    JyroString(value.ToStringValue()) :> JyroValue
                | _ -> raise (JyroRuntimeException(MessageCode.InvalidType,
                        sprintf "Cannot assign %A to variable '%s' of type string" value.ValueType variableName))
            | JyroValueType.Boolean ->
                match value with
                | :? JyroNumber as n ->
                    JyroBoolean.FromBoolean(abs n.Value > Double.Epsilon) :> JyroValue
                | :? JyroString as s ->
                    match s.Value.ToLowerInvariant() with
                    | "true" -> JyroBoolean.True :> JyroValue
                    | "false" -> JyroBoolean.False :> JyroValue
                    | _ -> raise (JyroRuntimeException(MessageCode.InvalidType,
                            sprintf "Cannot assign string to variable '%s' of type boolean" variableName))
                | _ -> raise (JyroRuntimeException(MessageCode.InvalidType,
                        sprintf "Cannot assign %A to variable '%s' of type boolean" value.ValueType variableName))
            | _ ->
                // Array, Object, Null - no cross-type coercion
                raise (JyroRuntimeException(MessageCode.InvalidType,
                    sprintf "Cannot assign %A to variable '%s' of type %A" value.ValueType variableName targetType))

/// Represents a null value (singleton)
and [<Sealed>] JyroNull private () =
    inherit JyroValue()

    static let instance = JyroNull()
    static member Instance = instance

    override _.ValueType = JyroValueType.Null
    override _.IsNull = true
    override _.ToObjectValue() = null
    override _.ToStringValue() = "null"
    override _.EqualsValue(_) = false
    override _.WriteJson(writer) = writer.WriteNullValue()

    override _.EvaluateBinary(op, right) =
        match op with
        | Add ->
            match right with
            | :? JyroString -> JyroString("null" + right.ToStringValue()) :> JyroValue
            | _ -> base.EvaluateBinary(op, right)
        | _ -> base.EvaluateBinary(op, right)

/// Represents a boolean value (flyweight pattern)
and [<Sealed>] JyroBoolean internal (value: bool) =
    inherit JyroValue()

    static let trueInstance = JyroBoolean(true)
    static let falseInstance = JyroBoolean(false)

    static member True = trueInstance
    static member False = falseInstance
    static member FromBoolean(value: bool) = if value then trueInstance else falseInstance

    member _.Value = value
    override _.ValueType = JyroValueType.Boolean
    override this.AsBoolean() = this
    override _.ToObjectValue() = box value
    override _.ToBoolean() = value
    override _.ToStringValue() = if value then "true" else "false"
    override _.WriteJson(writer) = writer.WriteBooleanValue(value)
    override _.EqualsValue(other) =
        match other with
        | :? JyroBoolean as b -> b.Value = value
        | _ -> false

    override this.EvaluateBinary(op, right) =
        match op with
        | Add ->
            match right with
            | :? JyroString -> JyroString(this.ToStringValue() + right.ToStringValue()) :> JyroValue
            | _ -> base.EvaluateBinary(op, right)
        | _ -> base.EvaluateBinary(op, right)

/// Represents a numeric value
and [<Sealed>] JyroNumber(value: float) =
    inherit JyroValue()

    let mutable forceFloat = abs(value % 1.0) >= Double.Epsilon

    member _.Value = value
    member _.IsInteger = abs(value % 1.0) < Double.Epsilon
    member _.ToInteger() = Convert.ToInt32(value)
    /// When true, serialization preserves float representation (e.g. 6.0 not 6)
    member _.ForceFloat with get() = forceFloat and set(v) = forceFloat <- v

    override _.ValueType = JyroValueType.Number
    override this.AsNumber() = this
    override this.ToObjectValue() =
        if this.IsInteger && not forceFloat then
            box (Convert.ToInt64(value))
        else
            box value
    override _.ToInt32() = Convert.ToInt32(value)
    override _.ToInt64() = Convert.ToInt64(value)
    override _.ToDouble() = value
    override _.ToBoolean() = abs value > Double.Epsilon
    override _.ToStringValue() = value.ToString()
    override this.WriteJson(writer) =
        if this.IsInteger && not forceFloat then
            writer.WriteNumberValue(Convert.ToInt64(value))
        else
            writer.WriteNumberValue(value)

    /// Create a JyroNumber from arithmetic (does not propagate ForceFloat)
    static member private FromArithmetic(result: float) =
        JyroNumber(result) :> JyroValue

    override this.EvaluateBinary(op, right) =
        match right with
        | :? JyroNumber as rn ->
            match op with
            | Add -> JyroNumber.FromArithmetic(value + rn.Value)
            | Subtract -> JyroNumber.FromArithmetic(value - rn.Value)
            | Multiply -> JyroNumber.FromArithmetic(value * rn.Value)
            | Divide ->
                if rn.Value = 0.0 then JyroError.raiseRuntime MessageCode.DivisionByZero Array.empty<obj>
                JyroNumber.FromArithmetic(value / rn.Value)
            | Modulo ->
                if rn.Value = 0.0 then JyroError.raiseRuntime MessageCode.ModuloByZero Array.empty<obj>
                JyroNumber.FromArithmetic(value % rn.Value)
            | LessThan -> JyroBoolean.FromBoolean(value < rn.Value) :> JyroValue
            | LessThanOrEqual -> JyroBoolean.FromBoolean(value <= rn.Value) :> JyroValue
            | GreaterThan -> JyroBoolean.FromBoolean(value > rn.Value) :> JyroValue
            | GreaterThanOrEqual -> JyroBoolean.FromBoolean(value >= rn.Value) :> JyroValue
            | _ -> base.EvaluateBinary(op, right)
        | :? JyroString when op = Add ->
            JyroString(value.ToString() + right.ToStringValue()) :> JyroValue
        | _ -> base.EvaluateBinary(op, right)

    override _.EvaluateUnary(op) =
        match op with
        | Negate -> JyroNumber(-value) :> JyroValue
        | _ -> base.EvaluateUnary(op)

    override _.EqualsValue(other) =
        match other with
        | :? JyroNumber as n -> abs(n.Value - value) < Double.Epsilon
        | _ -> false

/// Represents a string value
and [<Sealed>] JyroString(value: string) =
    inherit JyroValue()

    do if obj.ReferenceEquals(value, null) then raise (ArgumentNullException(nameof value))

    member _.Value = value
    member _.Length = value.Length
    member _.Item with get(index: int) =
        if index >= 0 && index < value.Length then
            JyroString(value.[index].ToString()) :> JyroValue
        else
            JyroNull.Instance :> JyroValue

    override _.ValueType = JyroValueType.String
    override this.AsString() = this
    override _.ToObjectValue() = box value
    override _.ToStringValue() = value
    override _.ToBoolean() = not (String.IsNullOrEmpty(value))
    override _.WriteJson(writer) = writer.WriteStringValue(value)

    override _.GetIndex(index) =
        match index with
        | :? JyroNumber as n ->
            let i = n.ToInteger()
            if i >= 0 && i < value.Length then
                JyroString(value.[i].ToString()) :> JyroValue
            else
                JyroNull.Instance :> JyroValue
        | _ -> JyroNull.Instance :> JyroValue

    override _.ToIterable() =
        value |> Seq.map (fun c -> JyroString(c.ToString()) :> JyroValue)

    override this.EvaluateBinary(op, right) =
        match op with
        | Add -> JyroString(value + right.ToStringValue()) :> JyroValue
        | _ ->
            match right with
            | :? JyroString as rs ->
                match op with
                | LessThan -> JyroBoolean.FromBoolean(String.Compare(value, rs.Value, StringComparison.Ordinal) < 0) :> JyroValue
                | LessThanOrEqual -> JyroBoolean.FromBoolean(String.Compare(value, rs.Value, StringComparison.Ordinal) <= 0) :> JyroValue
                | GreaterThan -> JyroBoolean.FromBoolean(String.Compare(value, rs.Value, StringComparison.Ordinal) > 0) :> JyroValue
                | GreaterThanOrEqual -> JyroBoolean.FromBoolean(String.Compare(value, rs.Value, StringComparison.Ordinal) >= 0) :> JyroValue
                | _ -> base.EvaluateBinary(op, right)
            | _ -> base.EvaluateBinary(op, right)

    override _.EqualsValue(other) =
        match other with
        | :? JyroString as s -> s.Value = value
        | _ -> false

/// Represents an array value
and [<Sealed>] JyroArray() =
    inherit JyroValue()

    let items = ResizeArray<JyroValue>()

    member _.Items = items :> IReadOnlyList<JyroValue>
    member _.Length = items.Count

    member _.Item
        with get(index: int) =
            if index >= 0 && index < items.Count then items.[index]
            else JyroNull.Instance
        and set(index: int) (value: JyroValue) =
            if index >= 0 then
                while items.Count <= index do
                    items.Add(JyroNull.Instance)
                items.[index] <- if obj.ReferenceEquals(value, null) then JyroNull.Instance :> JyroValue else value

    member _.Add(value: JyroValue) =
        items.Add(if obj.ReferenceEquals(value, null) then JyroNull.Instance :> JyroValue else value)

    member _.Insert(index: int, value: JyroValue) =
        items.Insert(index, if obj.ReferenceEquals(value, null) then JyroNull.Instance :> JyroValue else value)

    member _.RemoveAt(index: int) = items.RemoveAt(index)
    member _.Clear() = items.Clear()
    member _.Count() = items.Count

    static member FromEnumerable(source: IEnumerable<obj>) =
        let arr = JyroArray()
        for item in source do
            arr.Add(JyroValue.FromObject(item))
        arr

    override _.ValueType = JyroValueType.Array
    override this.AsArray() = this
    override _.ToIterable() = items |> Seq.cast<JyroValue>

    override _.GetIndex(index) =
        match index with
        | :? JyroNumber as n ->
            let i = n.ToInteger()
            if i >= 0 && i < items.Count then items.[i]
            else JyroNull.Instance
        | _ -> JyroNull.Instance

    override this.SetIndex(index, value) =
        match index with
        | :? JyroNumber as n ->
            let i = n.ToInteger()
            if i >= 0 then this.[i] <- value
        | _ -> ()

    override this.EvaluateBinary(op, right) =
        match op, right with
        | Add, (:? JyroArray as ra) ->
            let result = JyroArray()
            for item in items do result.Add(item)
            for item in ra.Items do result.Add(item)
            result :> JyroValue
        | _ -> base.EvaluateBinary(op, right)

    override _.ToObjectValue() =
        let result = ResizeArray<obj>(items.Count)
        for item in items do
            result.Add(item.ToObjectValue())
        result :> obj

    override _.ToStringValue() =
        sprintf "[%s]" (items |> Seq.map (fun i -> i.ToStringValue()) |> String.concat ", ")

    override _.ToBoolean() = items.Count > 0
    override _.WriteJson(writer) =
        writer.WriteStartArray()
        for item in items do
            item.WriteJson(writer)
        writer.WriteEndArray()

    override _.EqualsValue(other) =
        match other with
        | :? JyroArray as a when a.Items.Count = items.Count ->
            Seq.zip items a.Items |> Seq.forall (fun (l, r) -> l.EqualsValue(r))
        | _ -> false

    interface IEnumerable<JyroValue> with
        member _.GetEnumerator() = items.GetEnumerator() :> IEnumerator<JyroValue>
        member _.GetEnumerator() = items.GetEnumerator() :> System.Collections.IEnumerator

/// Represents an object value (key-value map)
and [<Sealed>] JyroObject() =
    inherit JyroValue()

    let properties = Dictionary<string, JyroValue>()

    member _.Properties = properties :> IReadOnlyDictionary<string, JyroValue>
    member _.Count = properties.Count

    member _.Item
        with get(key: string) =
            match properties.TryGetValue(key) with
            | true, v -> v
            | _ -> JyroNull.Instance
        and set(key: string) (value: JyroValue) =
            properties.[key] <- if obj.ReferenceEquals(value, null) then JyroNull.Instance :> JyroValue else value

    /// Gets a property value using literal key matching (no dot-path traversal)
    member _.GetPropertyLiteral(key: string) =
        match properties.TryGetValue(key) with
        | true, v -> v
        | _ -> JyroNull.Instance

    override this.GetProperty(key: string) =
        if key.Contains('.') then
            let segments = key.Split('.')
            let mutable current: JyroValue = this
            for segment in segments do
                match current with
                | :? JyroObject as obj ->
                    current <- obj.GetPropertyLiteral(segment)
                | _ ->
                    current <- JyroNull.Instance
            current
        else
            this.GetPropertyLiteral(key)

    override _.SetProperty(key: string, value: JyroValue) =
        properties.[key] <- if obj.ReferenceEquals(value, null) then JyroNull.Instance :> JyroValue else value

    override this.GetIndex(index) =
        match index with
        | :? JyroString as s -> this.GetPropertyLiteral(s.Value)
        | _ -> JyroNull.Instance

    override this.SetIndex(index, value) =
        match index with
        | :? JyroString as s -> this.SetProperty(s.Value, value)
        | _ -> ()

    override _.ToIterable() = properties.Values |> Seq.cast<JyroValue>

    member _.TryGet(key: string, value: outref<JyroValue>) =
        match properties.TryGetValue(key) with
        | true, v -> value <- v; true
        | _ -> value <- JyroNull.Instance; false

    member _.TryGetValue(key: string, value: outref<JyroValue>) =
        match properties.TryGetValue(key) with
        | true, v -> value <- v; true
        | _ -> value <- JyroNull.Instance; false

    member _.Remove(key: string) = properties.Remove(key)
    override _.RemoveProperty(key: string) = properties.Remove(key)
    member _.Clear() = properties.Clear()

    member _.ToDictionary() =
        let dict = Dictionary<string, obj>()
        for kvp in properties do
            dict.[kvp.Key] <- kvp.Value.ToObjectValue()
        dict

    static member FromDictionary(dict: IDictionary<string, obj>) =
        let obj = JyroObject()
        for kvp in dict do
            obj.SetProperty(kvp.Key, JyroValue.FromObject(kvp.Value))
        obj

    static member FromComplexObject(value: obj) =
        let obj = JyroObject()
        let props = value.GetType().GetProperties()
        for prop in props do
            if prop.CanRead then
                let propValue = prop.GetValue(value)
                obj.SetProperty(prop.Name, JyroValue.FromObject(propValue))
        obj

    override _.ValueType = JyroValueType.Object
    override this.AsObject() = this

    override _.ToObjectValue() =
        let dict = Dictionary<string, obj>()
        for kvp in properties do
            dict.[kvp.Key] <- kvp.Value.ToObjectValue()
        dict |> box

    override _.ToStringValue() =
        sprintf "{%s}" (properties |> Seq.map (fun kvp -> sprintf "%s: %s" kvp.Key (kvp.Value.ToStringValue())) |> String.concat ", ")
    override _.WriteJson(writer) =
        writer.WriteStartObject()
        for kvp in properties do
            writer.WritePropertyName(kvp.Key : string)
            kvp.Value.WriteJson(writer)
        writer.WriteEndObject()

    override _.ToBoolean() = properties.Count > 0

    override _.EqualsValue(other) =
        match other with
        | :? JyroObject as o when o.Count = properties.Count ->
            properties
            |> Seq.forall (fun kvp ->
                match o.Properties.TryGetValue(kvp.Key) with
                | true, v -> kvp.Value.EqualsValue(v)
                | _ -> false)
        | _ -> false

    interface IEnumerable<KeyValuePair<string, JyroValue>> with
        member _.GetEnumerator() = properties.GetEnumerator() :> IEnumerator<KeyValuePair<string, JyroValue>>
        member _.GetEnumerator() = properties.GetEnumerator() :> System.Collections.IEnumerator

/// Represents a lambda function value (used as inline arguments to higher-order stdlib functions).
/// The execution context is captured by the lambda closure, so Invoke only takes args.
and [<Sealed>] JyroFunction(invoke: Func<IReadOnlyList<JyroValue>, JyroValue>, paramCount: int) =
    inherit JyroValue()

    member _.ParamCount = paramCount
    member _.Invoke(args: IReadOnlyList<JyroValue>) = invoke.Invoke(args)

    /// Static factory method for creating JyroFunction values from expression trees.
    /// Using Expression.Call with a static method avoids Mono WASM interpreter issues
    /// with nested Expression.Lambda inside Expression.New.
    static member Create(invoke: Func<IReadOnlyList<JyroValue>, JyroValue>, paramCount: int) : JyroValue =
        JyroFunction(invoke, paramCount) :> JyroValue

    override _.ValueType = JyroValueType.Function
    override _.ToObjectValue() = box "<function>"
    override _.ToStringValue() = "<function>"
    override _.WriteJson(writer) = writer.WriteStringValue("<function>")
    override _.EqualsValue(other) = Object.ReferenceEquals(other, invoke)

    override this.EvaluateBinary(op, right) =
        match op with
        | Equal -> JyroBoolean.FromBoolean(Object.ReferenceEquals(this, right)) :> JyroValue
        | NotEqual -> JyroBoolean.FromBoolean(not (Object.ReferenceEquals(this, right))) :> JyroValue
        | _ -> base.EvaluateBinary(op, right)
