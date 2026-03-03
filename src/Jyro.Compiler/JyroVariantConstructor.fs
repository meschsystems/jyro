namespace Mesch.Jyro

open System.Collections.Generic

/// A variant constructor function generated from a union declaration.
/// Creates a tagged JyroObject with _variant discriminator and named fields.
type JyroVariantConstructor(variantName: string, unionName: string, signature: JyroFunctionSignature, fieldNames: string list) =

    /// The union this variant belongs to
    member _.UnionName = unionName

    /// The variant name (used as _variant discriminator value)
    member _.VariantName = variantName

    /// The field names for this variant (in order)
    member _.FieldNames = fieldNames

    interface IJyroFunction with
        member _.Name = variantName
        member _.Signature = signature
        member _.Execute(args: IReadOnlyList<JyroValue>, _ctx: JyroExecutionContext) =
            let obj = JyroObject()
            obj.SetProperty("_variant", JyroString(variantName))
            fieldNames |> List.iteri (fun i fieldName ->
                let value = if i < args.Count then args.[i] else JyroNull.Instance :> JyroValue
                obj.SetProperty(fieldName, value))
            obj :> JyroValue

module JyroVariantConstructor =
    /// Create a variant constructor from union name and variant definition
    let create (unionName: string) (variant: UnionVariant) : JyroVariantConstructor =
        let paramDefs =
            variant.Fields |> List.map (fun (pName, typeHint) ->
                Parameter.Required(pName, JyroUserFunction.paramTypeFromJyroType typeHint))
        let signature =
            { Name = variant.Name
              Parameters = paramDefs
              ReturnType = ObjectParam
              MinArgs = variant.Fields.Length
              MaxArgs = variant.Fields.Length }
        JyroVariantConstructor(variant.Name, unionName, signature, variant.Fields |> List.map fst)
