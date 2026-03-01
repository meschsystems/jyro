namespace Mesch.Jyro

open System
open System.Collections.Generic
open System.Security.Cryptography

/// Math functions
module MathFunctions =

    type AbsoluteFunction() =
        inherit JyroFunctionBase("Absolute", FunctionSignatures.unary "Absolute" NumberParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let n = this.GetNumberArgument(args, 0)
            JyroNumber(Math.Abs(n)) :> JyroValue

    type FloorFunction() =
        inherit JyroFunctionBase("Floor", FunctionSignatures.unary "Floor" NumberParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let n = this.GetNumberArgument(args, 0)
            JyroNumber(Math.Floor(n)) :> JyroValue

    type CeilingFunction() =
        inherit JyroFunctionBase("Ceiling", FunctionSignatures.unary "Ceiling" NumberParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let n = this.GetNumberArgument(args, 0)
            JyroNumber(Math.Ceiling(n)) :> JyroValue

    type MinFunction() =
        inherit JyroFunctionBase("Min", FunctionSignatures.unary "Min" ArrayParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let arr = this.GetArrayArgument(args, 0)
            let mutable minVal = Double.MaxValue
            let mutable found = false
            for item in arr.Items do
                match item with
                | :? JyroNumber as n ->
                    if n.Value < minVal then minVal <- n.Value
                    found <- true
                | _ -> ()
            if found then JyroNumber(minVal) :> JyroValue
            else JyroNull.Instance :> JyroValue

    type MaxFunction() =
        inherit JyroFunctionBase("Max", FunctionSignatures.unary "Max" ArrayParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let arr = this.GetArrayArgument(args, 0)
            let mutable maxVal = Double.MinValue
            let mutable found = false
            for item in arr.Items do
                match item with
                | :? JyroNumber as n ->
                    if n.Value > maxVal then maxVal <- n.Value
                    found <- true
                | _ -> ()
            if found then JyroNumber(maxVal) :> JyroValue
            else JyroNull.Instance :> JyroValue

    type PowerFunction() =
        inherit JyroFunctionBase("Power", FunctionSignatures.binary "Power" NumberParam NumberParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let baseVal = this.GetNumberArgument(args, 0)
            let exp = this.GetNumberArgument(args, 1)
            JyroNumber(Math.Pow(baseVal, exp)) :> JyroValue

    type SquareRootFunction() =
        inherit JyroFunctionBase("SquareRoot", FunctionSignatures.unary "SquareRoot" NumberParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let n = this.GetNumberArgument(args, 0)
            JyroNumber(Math.Sqrt(n)) :> JyroValue

    type LogFunction() =
        inherit JyroFunctionBase("Log",
            FunctionSignatures.create "Log"
                [ Parameter.Required("value", NumberParam)
                  Parameter.Optional("base", NumberParam) ]
                NumberParam)
        override this.ExecuteImpl(args, _) =
            let n = this.GetNumberArgument(args, 0)
            if args.Count > 1 then
                let baseVal = this.GetNumberArgument(args, 1)
                JyroNumber(Math.Log(n, baseVal)) :> JyroValue
            else
                JyroNumber(Math.Log(n)) :> JyroValue

    type SumFunction() =
        inherit JyroFunctionBase("Sum", FunctionSignatures.unary "Sum" ArrayParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let arr = this.GetArrayArgument(args, 0)
            let mutable sum = 0.0
            for item in arr.Items do
                match item with
                | :? JyroNumber as n ->
                    sum <- sum + n.Value
                | _ -> ()
            JyroNumber(sum) :> JyroValue

    type AverageFunction() =
        inherit JyroFunctionBase("Average", FunctionSignatures.unary "Average" ArrayParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let arr = this.GetArrayArgument(args, 0)
            if arr.Items.Count = 0 then
                JyroNull.Instance :> JyroValue
            else
                let mutable sum = 0.0
                let mutable count = 0
                for item in arr.Items do
                    match item with
                    | :? JyroNumber as n ->
                        sum <- sum + n.Value
                        count <- count + 1
                    | _ -> ()
                if count > 0 then
                    JyroNumber(sum / float count) :> JyroValue
                else JyroNull.Instance :> JyroValue


    type ClampFunction() =
        inherit JyroFunctionBase("Clamp",
            FunctionSignatures.create "Clamp"
                [ Parameter.Required("value", NumberParam)
                  Parameter.Required("min", NumberParam)
                  Parameter.Required("max", NumberParam) ]
                NumberParam)
        override this.ExecuteImpl(args, _) =
            let valueArg = this.GetArgument<JyroNumber>(args, 0)
            let minArg = this.GetArgument<JyroNumber>(args, 1)
            let maxArg = this.GetArgument<JyroNumber>(args, 2)
            JyroNumber(Math.Clamp(valueArg.Value, minArg.Value, maxArg.Value)) :> JyroValue

    type MedianFunction() =
        inherit JyroFunctionBase("Median", FunctionSignatures.unary "Median" ArrayParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let arr = this.GetArrayArgument(args, 0)
            let numbers =
                arr.Items
                |> Seq.choose (fun item ->
                    match item with
                    | :? JyroNumber as n -> Some n.Value
                    | _ -> None)
                |> Seq.toArray
            if numbers.Length = 0 then
                JyroNull.Instance :> JyroValue
            else
                Array.sortInPlace numbers
                let mid = numbers.Length / 2
                if numbers.Length % 2 = 0 then
                    JyroNumber((numbers.[mid - 1] + numbers.[mid]) / 2.0) :> JyroValue
                else
                    JyroNumber(numbers.[mid]) :> JyroValue

    type ModeFunction() =
        inherit JyroFunctionBase("Mode", FunctionSignatures.unary "Mode" ArrayParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let arr = this.GetArrayArgument(args, 0)
            let numbers =
                arr.Items
                |> Seq.choose (fun item ->
                    match item with
                    | :? JyroNumber as n -> Some n.Value
                    | _ -> None)
                |> Seq.toList
            if numbers.IsEmpty then
                JyroNull.Instance :> JyroValue
            else
                let counts = Dictionary<float, int>()
                for n in numbers do
                    match counts.TryGetValue(n) with
                    | true, c -> counts.[n] <- c + 1
                    | _ -> counts.[n] <- 1
                let mutable maxCount = 0
                let mutable modeVal = 0.0
                for kvp in counts do
                    if kvp.Value > maxCount then
                        maxCount <- kvp.Value
                        modeVal <- kvp.Key
                JyroNumber(modeVal) :> JyroValue

    type RandomIntFunction() =
        inherit JyroFunctionBase("RandomInt",
            FunctionSignatures.binary "RandomInt" NumberParam NumberParam NumberParam)
        override this.ExecuteImpl(args, _) =
            let minVal = int (this.GetNumberArgument(args, 0))
            let maxVal = int (this.GetNumberArgument(args, 1))
            JyroNumber(float (RandomNumberGenerator.GetInt32(minVal, maxVal + 1))) :> JyroValue

    /// Get all math functions
    let getAll () : IJyroFunction list =
        [ AbsoluteFunction()
          FloorFunction()
          CeilingFunction()
          MinFunction()
          MaxFunction()
          PowerFunction()
          SquareRootFunction()
          LogFunction()
          SumFunction()
          AverageFunction()
          ClampFunction()
          MedianFunction()
          ModeFunction()
          RandomIntFunction() ]
