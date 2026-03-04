namespace Mesch.Jyro

open System
open System.Linq.Expressions
open System.Collections.Generic

/// Compiles Jyro expressions to LINQ Expression Trees
module ExpressionCompiler =
    /// Compilation context for tracking variables, functions, and execution context
    type CompilationContext =
        { Variables: Map<string, ParameterExpression>
          VariableTypes: Map<string, JyroType>
          Functions: Map<string, IJyroFunction>
          UnionDefinitions: Map<string, UnionVariant list>
          VariantToUnion: Map<string, string>
          DataParam: ParameterExpression
          ContextParam: ParameterExpression
          BreakLabel: LabelTarget option
          ContinueLabel: LabelTarget option
          ReturnLabel: LabelTarget option
          InFunction: bool }

        static member Create(functions: Map<string, IJyroFunction>, ?unionDefs: Map<string, UnionVariant list>, ?variantMap: Map<string, string>) =
            let dataParam = Expression.Parameter(typeof<JyroValue>, "Data")
            let contextParam = Expression.Parameter(typeof<JyroExecutionContext>, "ctx")
            { Variables = Map.empty
              VariableTypes = Map.empty
              Functions = functions
              UnionDefinitions = defaultArg unionDefs Map.empty
              VariantToUnion = defaultArg variantMap Map.empty
              DataParam = dataParam
              ContextParam = contextParam
              BreakLabel = None
              ContinueLabel = None
              ReturnLabel = None
              InFunction = false }

        member this.WithVariable(name: string, param: ParameterExpression) =
            { this with Variables = this.Variables.Add(name, param) }

        member this.WithVariableType(name: string, jyroType: JyroType) =
            { this with VariableTypes = this.VariableTypes.Add(name, jyroType) }

        member this.WithLoopLabels(breakLabel: LabelTarget, continueLabel: LabelTarget) =
            { this with BreakLabel = Some breakLabel; ContinueLabel = Some continueLabel }

        member this.WithReturnLabel(returnLabel: LabelTarget) =
            { this with ReturnLabel = Some returnLabel }

    /// Get the method info for a JyroValue operation
    let private getJyroValueMethod (name: string) =
        typeof<JyroValue>.GetMethod(name)

    // Cached MethodInfo for execution context resource checks
    let private checkAndEnterCallMethod = typeof<JyroExecutionContext>.GetMethod("CheckAndEnterCall")
    let private exitCallMethod = typeof<JyroExecutionContext>.GetMethod("ExitCall")

    // Cached MethodInfo for type hint coercion
    let private coerceToTypeMethod = typeof<JyroValue>.GetMethod("CoerceToType")

    // Cached MethodInfo and constant for increment/decrement operations
    let private evaluateBinaryMethod = typeof<JyroValue>.GetMethod("EvaluateBinary")
    let private getPropertyMethod = typeof<JyroValue>.GetMethod("GetProperty")
    let private setPropertyMethod = typeof<JyroValue>.GetMethod("SetProperty")
    let private getIndexMethod = typeof<JyroValue>.GetMethod("GetIndex")
    let private setIndexMethod = typeof<JyroValue>.GetMethod("SetIndex")
    let private oneConst = Expression.Constant(JyroNumber(1.0) :> JyroValue, typeof<JyroValue>)

    /// Compile a literal value to an expression
    let private compileLiteral (value: JyroValue) : Expression =
        Expression.Constant(value, typeof<JyroValue>) :> Expression

    /// Compile an identifier reference
    let private compileIdentifier (ctx: CompilationContext) (name: string) : Expression =
        if name = "Data" then
            ctx.DataParam :> Expression
        else
            match ctx.Variables.TryFind(name) with
            | Some param -> param :> Expression
            | None -> failwithf "Undefined variable: %s" name

    /// Compile a binary operation
    let rec private compileBinary (ctx: CompilationContext) (left: Expr) (op: BinaryOp) (right: Expr) : Expression =
        match op with
        | And ->
            // Short-circuit: if left is falsy return left, else return right
            let leftExpr = compileExpr ctx left
            let leftVar = Expression.Variable(typeof<JyroValue>, "andLeft")
            let truthinessMethod = getJyroValueMethod "ToBooleanTruthiness"
            let testExpr = Expression.Call(leftVar, truthinessMethod)
            let rightExpr = compileExpr ctx right
            Expression.Block(
                [| leftVar |],
                [| Expression.Assign(leftVar, leftExpr) :> Expression
                   Expression.Condition(testExpr, rightExpr, leftVar :> Expression, typeof<JyroValue>) :> Expression |]) :> Expression
        | Or ->
            // Short-circuit: if left is truthy return left, else return right
            let leftExpr = compileExpr ctx left
            let leftVar = Expression.Variable(typeof<JyroValue>, "orLeft")
            let truthinessMethod = getJyroValueMethod "ToBooleanTruthiness"
            let testExpr = Expression.Call(leftVar, truthinessMethod)
            let rightExpr = compileExpr ctx right
            Expression.Block(
                [| leftVar |],
                [| Expression.Assign(leftVar, leftExpr) :> Expression
                   Expression.Condition(testExpr, leftVar :> Expression, rightExpr, typeof<JyroValue>) :> Expression |]) :> Expression
        | _ ->
            let leftExpr = compileExpr ctx left
            let rightExpr = compileExpr ctx right
            let methodName = "EvaluateBinary"
            let opValue = Expression.Constant(op, typeof<BinaryOp>)
            Expression.Call(leftExpr, getJyroValueMethod methodName, opValue, rightExpr) :> Expression

    /// Compile a unary operation
    and private compileUnary (ctx: CompilationContext) (op: UnaryOp) (operand: Expr) : Expression =
        let operandExpr = compileExpr ctx operand
        let methodName = "EvaluateUnary"
        let opValue = Expression.Constant(op, typeof<UnaryOp>)
        Expression.Call(operandExpr, getJyroValueMethod methodName, opValue) :> Expression

    /// Compile a ternary/conditional expression
    and private compileTernary (ctx: CompilationContext) (cond: Expr) (thenExpr: Expr) (elseExpr: Expr) : Expression =
        let condExpr = compileExpr ctx cond
        let truthinessMethod = getJyroValueMethod "ToBooleanTruthiness"
        let testExpr = Expression.Call(condExpr, truthinessMethod)
        let thenCompiled = compileExpr ctx thenExpr
        let elseCompiled = compileExpr ctx elseExpr
        Expression.Condition(testExpr, thenCompiled, elseCompiled) :> Expression

    /// Compile a function call with call depth tracking
    and private compileCall (ctx: CompilationContext) (name: string) (callArgs: CallArgs) : Expression =
        match ctx.Functions.TryFind(name) with
        | Some func ->
            let compiledArgs =
                match callArgs with
                | PositionalArgs exprs ->
                    exprs |> List.map (compileExpr ctx) |> Array.ofList
                | NamedArgs pairs ->
                    // Reorder named args into parameter-declaration order, trim trailing omitted
                    let argMap = pairs |> Map.ofList
                    let reordered =
                        func.Signature.Parameters
                        |> List.map (fun param ->
                            match argMap.TryFind(param.Name) with
                            | Some expr -> Some (compileExpr ctx expr)
                            | None -> None)
                    // Find last provided arg index; trim trailing omitted so args.Count is correct
                    let lastIdx =
                        reordered
                        |> List.mapi (fun i opt -> (i, opt))
                        |> List.filter (fun (_, opt) -> opt.IsSome)
                        |> List.tryLast
                        |> Option.map fst
                        |> Option.defaultValue -1
                    reordered
                    |> List.take (lastIdx + 1)
                    |> List.map (Option.defaultValue (Expression.Constant(JyroNull.Instance, typeof<JyroValue>) :> Expression))
                    |> Array.ofList
            let argsArray = Expression.NewArrayInit(typeof<JyroValue>, compiledArgs)
            let funcConst = Expression.Constant(func, typeof<IJyroFunction>)
            let executeMethod = typeof<IJyroFunction>.GetMethod("Execute")
            let actualCall = Expression.Call(funcConst, executeMethod, argsArray, ctx.ContextParam)

            // Wrap with call depth tracking: CheckAndEnterCall -> try call finally ExitCall
            let resultVar = Expression.Variable(typeof<JyroValue>, "callResult")
            Expression.Block(
                [| resultVar |],
                [| Expression.Call(ctx.ContextParam, checkAndEnterCallMethod) :> Expression
                   Expression.TryFinally(
                       Expression.Assign(resultVar, actualCall),
                       Expression.Call(ctx.ContextParam, exitCallMethod)) :> Expression
                   resultVar :> Expression |]) :> Expression
        | None -> failwithf "Undefined function: %s" name

    /// Compile a property access
    and private compilePropertyAccess (ctx: CompilationContext) (target: Expr) (prop: string) : Expression =
        let targetExpr = compileExpr ctx target
        let propConst = Expression.Constant(prop, typeof<string>)
        Expression.Call(targetExpr, getJyroValueMethod "GetProperty", propConst) :> Expression

    /// Compile an index access
    and private compileIndexAccess (ctx: CompilationContext) (target: Expr) (index: Expr) : Expression =
        let targetExpr = compileExpr ctx target
        let indexExpr = compileExpr ctx index
        Expression.Call(targetExpr, getJyroValueMethod "GetIndex", indexExpr) :> Expression

    /// Compile an object literal
    and private compileObjectLiteral (ctx: CompilationContext) (props: (string * Expr) list) : Expression =
        let objVar = Expression.Variable(typeof<JyroObject>, "obj")
        let newObj = Expression.New(typeof<JyroObject>)
        let assigns = props |> List.map (fun (key, value) ->
            let keyConst = Expression.Constant(key, typeof<string>)
            let valueExpr = compileExpr ctx value
            Expression.Call(objVar, typeof<JyroObject>.GetMethod("SetProperty"), keyConst, valueExpr) :> Expression)
        let body = Expression.Block(
            [| objVar |],
            [ yield Expression.Assign(objVar, newObj) :> Expression
              yield! assigns
              yield objVar :> Expression ] |> Array.ofList)
        body :> Expression

    /// Compile an array literal
    and private compileArrayLiteral (ctx: CompilationContext) (elements: Expr list) : Expression =
        let arrVar = Expression.Variable(typeof<JyroArray>, "arr")
        let newArr = Expression.New(typeof<JyroArray>)
        let adds = elements |> List.map (fun elem ->
            let elemExpr = compileExpr ctx elem
            Expression.Call(arrVar, typeof<JyroArray>.GetMethod("Add"), elemExpr) :> Expression)
        let body = Expression.Block(
            [| arrVar |],
            [ yield Expression.Assign(arrVar, newArr) :> Expression
              yield! adds
              yield arrVar :> Expression ] |> Array.ofList)
        body :> Expression

    /// Map JyroType (AST) to JyroValueType (runtime)
    and jyroTypeToValueType (jyroType: JyroType) : JyroValueType =
        match jyroType with
        | NumberType -> JyroValueType.Number
        | StringType -> JyroValueType.String
        | BooleanType -> JyroValueType.Boolean
        | ObjectType -> JyroValueType.Object
        | ArrayType -> JyroValueType.Array
        | NullType -> JyroValueType.Null
        | AnyType -> JyroValueType.Null // Any always matches, handled specially

    /// Compile a lambda expression to a JyroFunction value
    and private compileLambda (ctx: CompilationContext) (params': string list) (body: Expr) : Expression =
        // Create the args parameter for the lambda delegate
        let argsParam = Expression.Parameter(typeof<IReadOnlyList<JyroValue>>, "lambdaArgs")

        // Create local variables for each named parameter, initialized from args[i]
        let itemGetter = typeof<IReadOnlyList<JyroValue>>.GetMethod("get_Item")
        let paramVars = params' |> List.mapi (fun i name ->
            let var = Expression.Variable(typeof<JyroValue>, name)
            let getItem = Expression.Call(argsParam, itemGetter, Expression.Constant(i)) :> Expression
            let init = Expression.Assign(var, getItem) :> Expression
            (name, var, init))

        // Build inner context: lambda params + captured outer variables
        // ContextParam stays the same (captured via closure), break/continue/return cleared
        let innerCtx =
            paramVars |> List.fold (fun c (name, var, _) ->
                { c with Variables = c.Variables.Add(name, var) })
                { ctx with
                    BreakLabel = None
                    ContinueLabel = None
                    ReturnLabel = None }

        // Compile the body expression in the inner context
        let bodyExpr = compileExpr innerCtx body

        // Create the lambda body block: initialize params then evaluate body
        let vars = paramVars |> List.map (fun (_, var, _) -> var) |> Array.ofList
        let inits = paramVars |> List.map (fun (_, _, init) -> init)
        let allExprs = [ yield! inits; yield bodyExpr ] |> Array.ofList
        let blockBody = Expression.Block(typeof<JyroValue>, vars, allExprs)

        // Create the LINQ lambda expression (execution context captured via outer scope closure)
        let lambdaExpr = Expression.Lambda<Func<IReadOnlyList<JyroValue>, JyroValue>>(
            blockBody, argsParam)

        // Pre-compile the inner lambda at tree-build time and embed as a constant.
        // This avoids nested LambdaExpression compilation issues in Mono WASM's LightCompiler.
        try
            let compiled = lambdaExpr.Compile()
            let jyroFunc = JyroFunction(compiled, params'.Length) :> JyroValue
            Expression.Constant(jyroFunc, typeof<JyroValue>) :> Expression
        with :? InvalidOperationException ->
            // Lambda captures outer scope variables - must embed in tree for runtime compilation
            let createMethod = typeof<JyroFunction>.GetMethod("Create",
                [| typeof<Func<IReadOnlyList<JyroValue>, JyroValue>>; typeof<int> |])
            Expression.Call(createMethod, lambdaExpr, Expression.Constant(params'.Length)) :> Expression

    /// Compile an increment/decrement expression (++x, x++, --x, x--)
    and private compileIncrementDecrement (ctx: CompilationContext) (expr: Expr) (isIncrement: bool) (isPrefix: bool) : Expression =
        let binaryOp =
            if isIncrement then Expression.Constant(Add, typeof<BinaryOp>)
            else Expression.Constant(Subtract, typeof<BinaryOp>)

        match expr with
        | Identifier(name, _) when name <> "Data" ->
            match ctx.Variables.TryFind(name) with
            | Some varExpr ->
                let newVal = Expression.Variable(typeof<JyroValue>, "incNew")
                let computeNew = Expression.Assign(newVal, Expression.Call(varExpr, evaluateBinaryMethod, binaryOp, oneConst)) :> Expression
                // Apply type coercion if the variable has a type hint
                let coercedNew =
                    match ctx.VariableTypes.TryFind(name) with
                    | Some hint when hint <> AnyType ->
                        let targetType = jyroTypeToValueType hint
                        Expression.Call(coerceToTypeMethod,
                            newVal :> Expression,
                            Expression.Constant(targetType, typeof<JyroValueType>),
                            Expression.Constant(name, typeof<string>)) :> Expression
                    | _ -> newVal :> Expression
                if isPrefix then
                    Expression.Block(
                        [| newVal |],
                        [| computeNew
                           Expression.Assign(varExpr, coercedNew) :> Expression
                           varExpr :> Expression |]) :> Expression
                else
                    let oldVal = Expression.Variable(typeof<JyroValue>, "incOld")
                    Expression.Block(
                        [| oldVal; newVal |],
                        [| Expression.Assign(oldVal, varExpr :> Expression) :> Expression
                           computeNew
                           Expression.Assign(varExpr, coercedNew) :> Expression
                           oldVal :> Expression |]) :> Expression
            | None -> failwithf "Undefined variable: %s" name

        | PropertyAccess(obj, prop, _) ->
            let objExpr = compileExpr ctx obj
            let propConst = Expression.Constant(prop, typeof<string>)
            let objVar = Expression.Variable(typeof<JyroValue>, "incObj")
            let oldVal = Expression.Variable(typeof<JyroValue>, "incOld")
            let newVal = Expression.Variable(typeof<JyroValue>, "incNew")
            Expression.Block(
                [| objVar; oldVal; newVal |],
                [| Expression.Assign(objVar, objExpr) :> Expression
                   Expression.Assign(oldVal, Expression.Call(objVar, getPropertyMethod, propConst)) :> Expression
                   Expression.Assign(newVal, Expression.Call(oldVal, evaluateBinaryMethod, binaryOp, oneConst)) :> Expression
                   Expression.Call(objVar, setPropertyMethod, propConst, newVal) :> Expression
                   (if isPrefix then newVal else oldVal) :> Expression |]) :> Expression

        | IndexAccess(obj, index, _) ->
            let objExpr = compileExpr ctx obj
            let indexExpr = compileExpr ctx index
            let objVar = Expression.Variable(typeof<JyroValue>, "incObj")
            let idxVar = Expression.Variable(typeof<JyroValue>, "incIdx")
            let oldVal = Expression.Variable(typeof<JyroValue>, "incOld")
            let newVal = Expression.Variable(typeof<JyroValue>, "incNew")
            Expression.Block(
                [| objVar; idxVar; oldVal; newVal |],
                [| Expression.Assign(objVar, objExpr) :> Expression
                   Expression.Assign(idxVar, indexExpr) :> Expression
                   Expression.Assign(oldVal, Expression.Call(objVar, getIndexMethod, idxVar)) :> Expression
                   Expression.Assign(newVal, Expression.Call(oldVal, evaluateBinaryMethod, binaryOp, oneConst)) :> Expression
                   Expression.Call(objVar, setIndexMethod, idxVar, newVal) :> Expression
                   (if isPrefix then newVal else oldVal) :> Expression |]) :> Expression

        | _ -> failwith "Invalid increment/decrement target"

    /// Compile a type check expression (x is type / x is not type)
    and private compileTypeCheck (ctx: CompilationContext) (expr: Expr) (jyroType: JyroType) (isNegated: bool) : Expression =
        let exprCompiled = compileExpr ctx expr
        // Get the ValueType property
        let valueTypeProp = typeof<JyroValue>.GetProperty("ValueType")
        let valueTypeExpr = Expression.Property(exprCompiled, valueTypeProp)
        // Compare against expected type
        let expectedType = jyroTypeToValueType jyroType
        let expectedTypeConst = Expression.Constant(expectedType, typeof<JyroValueType>)
        let comparison =
            if jyroType = AnyType then
                // "is any" always true
                Expression.Constant(true) :> Expression
            else
                Expression.Equal(valueTypeExpr, expectedTypeConst) :> Expression
        // Apply negation if needed
        let result =
            if isNegated then
                Expression.Not(comparison) :> Expression
            else
                comparison
        // Convert bool to JyroBoolean
        let fromBoolMethod = typeof<JyroBoolean>.GetMethod("FromBoolean")
        Expression.Call(fromBoolMethod, result) :> Expression

    /// Compile a match expression as a chain of conditionals returning JyroValue
    and private compileMatchExpr (ctx: CompilationContext) (expr: Expr) (cases: MatchExprCase list) : Expression =
        let matchExpr = compileExpr ctx expr
        let matchVar = Expression.Variable(typeof<JyroValue>, "matchVal")
        let assignMatch = Expression.Assign(matchVar, matchExpr) :> Expression

        // Extract _variant: matchVal.GetProperty("_variant").ToStringValue()
        let getPropertyMethod = typeof<JyroValue>.GetMethod("GetProperty")
        let toStringMethod = typeof<JyroValue>.GetMethod("ToStringValue")
        let variantTagExpr =
            Expression.Call(
                Expression.Call(matchVar, getPropertyMethod, Expression.Constant("_variant", typeof<string>)),
                toStringMethod)
        let variantTagVar = Expression.Variable(typeof<string>, "variantTag")
        let assignTag = Expression.Assign(variantTagVar, variantTagExpr) :> Expression

        let rec buildCases (remaining: MatchExprCase list) : Expression =
            match remaining with
            | [] -> Expression.Constant(JyroNull.Instance, typeof<JyroValue>) :> Expression
            | case :: rest ->
                let testExpr =
                    Expression.Equal(
                        variantTagVar,
                        Expression.Constant(case.VariantName, typeof<string>)) :> Expression

                // Destructure fields
                let unionName = ctx.VariantToUnion.[case.VariantName]
                let variants = ctx.UnionDefinitions.[unionName]
                let variant = variants |> List.find (fun v -> v.Name = case.VariantName)
                let fieldNames = variant.Fields |> List.map fst

                // Create binding variables and initialize from matchVar properties
                let bindingVars = ResizeArray<ParameterExpression>()
                let bindingInits = ResizeArray<Expression>()
                let mutable caseCtx = ctx
                for (bindingName, fieldName) in List.zip case.Bindings fieldNames do
                    let bindVar = Expression.Variable(typeof<JyroValue>, bindingName)
                    bindingVars.Add(bindVar)
                    let getField = Expression.Call(matchVar, getPropertyMethod, Expression.Constant(fieldName, typeof<string>))
                    bindingInits.Add(Expression.Assign(bindVar, getField) :> Expression)
                    caseCtx <- caseCtx.WithVariable(bindingName, bindVar)

                // Compile case body expression with bindings in scope
                let bodyExpr = compileExpr caseCtx case.Body
                let caseExpr =
                    if bindingVars.Count > 0 then
                        let allExprs = ResizeArray<Expression>()
                        allExprs.AddRange(bindingInits)
                        allExprs.Add(bodyExpr)
                        Expression.Block(typeof<JyroValue>, bindingVars, allExprs) :> Expression
                    else
                        bodyExpr

                let elseExpr = buildCases rest
                Expression.Condition(testExpr, caseExpr, elseExpr, typeof<JyroValue>) :> Expression

        let chain = buildCases cases
        Expression.Block(typeof<JyroValue>, [| matchVar; variantTagVar |], [| assignMatch; assignTag; chain |]) :> Expression

    /// Compile an expression to an Expression Tree
    and compileExpr (ctx: CompilationContext) (expr: Expr) : Expression =
        match expr with
        | Literal(value, _) -> compileLiteral value
        | Identifier(name, _) -> compileIdentifier ctx name
        | Binary(left, op, right, _) -> compileBinary ctx left op right
        | Unary(op, operand, _) -> compileUnary ctx op operand
        | Ternary(cond, thenExpr, elseExpr, _) -> compileTernary ctx cond thenExpr elseExpr
        | Call(name, callArgs, _) -> compileCall ctx name callArgs
        | PropertyAccess(target, prop, _) -> compilePropertyAccess ctx target prop
        | IndexAccess(target, index, _) -> compileIndexAccess ctx target index
        | ObjectLiteral(props, _) -> compileObjectLiteral ctx props
        | ArrayLiteral(elements, _) -> compileArrayLiteral ctx elements
        | Lambda(params', body, _) -> compileLambda ctx params' body
        | TypeCheck(expr, jyroType, isNegated, _) -> compileTypeCheck ctx expr jyroType isNegated
        | IncrementDecrement(expr, isInc, isPre, _) -> compileIncrementDecrement ctx expr isInc isPre
        | MatchExpr(expr, cases, _) -> compileMatchExpr ctx expr cases
