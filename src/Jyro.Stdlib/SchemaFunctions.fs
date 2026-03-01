namespace Mesch.Jyro

open System.Text.Json
open Json.Schema

/// Schema validation functions
module SchemaFunctions =

    type ValidateRequiredFunction() =
        inherit JyroFunctionBase("ValidateRequired",
            FunctionSignatures.create "ValidateRequired"
                [ Parameter.Required("obj", ObjectParam)
                  Parameter.Required("fields", ArrayParam) ]
                ObjectParam)
        override this.ExecuteImpl(args, _) =
            let obj = this.GetObjectArgument(args, 0)
            let fields = this.GetArrayArgument(args, 1)
            let result = JyroObject()
            let errors = JyroArray()
            let mutable isValid = true

            for field in fields.Items do
                match field with
                | :? JyroString as s ->
                    let fieldName = s.Value
                    if not (obj.Properties.ContainsKey(fieldName)) then
                        isValid <- false
                        errors.Add(JyroString(sprintf "Missing required field: %s" fieldName))
                    elif obj.Properties.[fieldName].ValueType = JyroValueType.Null then
                        isValid <- false
                        errors.Add(JyroString(sprintf "Field is null: %s" fieldName))
                    else
                        match obj.Properties.[fieldName] with
                        | :? JyroString as str when System.String.IsNullOrWhiteSpace(str.Value) ->
                            isValid <- false
                            errors.Add(JyroString(sprintf "Field is empty: %s" fieldName))
                        | _ -> ()
                | _ -> ()

            result.SetProperty("valid", JyroBoolean.FromBoolean(isValid))
            result.SetProperty("errors", errors)
            result :> JyroValue

    type ValidateSchemaFunction() =
        inherit JyroFunctionBase("ValidateSchema",
            FunctionSignatures.create "ValidateSchema"
                [ Parameter.Required("data", AnyParam)
                  Parameter.Required("schema", ObjectParam) ]
                ArrayParam)

        static member private CollectErrors(result: EvaluationResults, errors: JyroArray) =
            if result.Errors <> null && result.Errors.Count > 0 then
                for error in result.Errors do
                    let errorObj = JyroObject()
                    errorObj.["path"] <- JyroString(result.InstanceLocation.ToString())
                    errorObj.["keyword"] <- JyroString(error.Key)
                    errorObj.["message"] <- JyroString(error.Value)
                    errors.Add(errorObj)
            if result.Details <> null then
                for detail in result.Details do
                    if not detail.IsValid then
                        ValidateSchemaFunction.CollectErrors(detail, errors)

        override this.ExecuteImpl(args, _) =
            let data = args.[0]
            let schemaObj = this.GetObjectArgument(args, 1)
            let schemaJson = schemaObj.ToJson()
            let schema = JsonSchema.FromText(schemaJson)
            let dataJson = data.ToJson()
            use document = JsonDocument.Parse(dataJson)
            let options = EvaluationOptions(OutputFormat = OutputFormat.List)
            let result = schema.Evaluate(document.RootElement, options)
            let errors = JyroArray()
            if not result.IsValid then
                ValidateSchemaFunction.CollectErrors(result, errors)
            errors :> JyroValue

    /// Get all schema functions
    let getAll () : IJyroFunction list =
        [ ValidateRequiredFunction()
          ValidateSchemaFunction() ]
