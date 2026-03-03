namespace Mesch.Jyro

open System
open System.Linq.Expressions
open Mesch.Jyro.ExpressionCompiler

/// Compiles Jyro statements to LINQ Expression Trees
module StatementCompiler =
    // Cached MethodInfo for execution context resource checks
    let private checkStatementMethod = typeof<JyroExecutionContext>.GetMethod("CheckAndCountStatement")
    let private checkLoopMethod = typeof<JyroExecutionContext>.GetMethod("CheckAndEnterLoop")
    let private setReturnMessageMethod = typeof<JyroExecutionContext>.GetMethod("SetReturnMessage")

    /// Wrap a statement expression with a preceding statement count check
    let private withStatementCheck (ctx: CompilationContext) (expr: Expression) : Expression =
        Expression.Block(
            Expression.Call(ctx.ContextParam, checkStatementMethod),
            expr) :> Expression

    // Cached constructor and property accessors for location tracking
    let private rteCtor = typeof<JyroRuntimeException>.GetConstructor(
        [| typeof<MessageCode>; typeof<string>; typeof<obj[]>; typeof<int>; typeof<int> |])
    let private rteCodeProp = typeof<JyroRuntimeException>.GetProperty("Code")
    let private rteMessageProp = typeof<Exception>.GetProperty("Message")
    let private rteArgsProp = typeof<JyroRuntimeException>.GetProperty("Args")
    let private rteHasLocationProp = typeof<JyroRuntimeException>.GetProperty("HasLocation")

    /// Wrap a statement expression with try/catch that injects source location into runtime exceptions
    let private withLocationTracking (body: Expression) (pos: Position) : Expression =
        if pos.Line = 0 then body
        else
            // Catch JyroRuntimeException without location and re-throw with position (preserving Args)
            let rteParam = Expression.Parameter(typeof<JyroRuntimeException>, "rtex")
            let rteFilter = Expression.Not(Expression.Property(rteParam, rteHasLocationProp))
            let rteNewEx = Expression.New(rteCtor,
                            Expression.Property(rteParam, rteCodeProp),
                            Expression.Property(rteParam, rteMessageProp),
                            Expression.Property(rteParam, rteArgsProp),
                            Expression.Constant(pos.Line),
                            Expression.Constant(pos.Column))
            let rteCatch = Expression.Catch(rteParam, Expression.Throw(rteNewEx, body.Type), rteFilter)

            // Re-throw JyroRuntimeException that already has location (skip wrapping)
            let rtePassParam = Expression.Parameter(typeof<JyroRuntimeException>, "rtep")
            let rtePassFilter = Expression.Property(rtePassParam, rteHasLocationProp)
            let rtePassCatch = Expression.Catch(rtePassParam, Expression.Rethrow(body.Type), rtePassFilter)

            // Catch general exceptions and wrap as JyroRuntimeException with position
            let exParam = Expression.Parameter(typeof<Exception>, "ex")
            let exNewRte = Expression.New(rteCtor,
                            Expression.Constant(MessageCode.RuntimeError),
                            Expression.Property(exParam, typeof<Exception>.GetProperty("Message")),
                            Expression.Constant(Array.empty<obj>, typeof<obj[]>),
                            Expression.Constant(pos.Line),
                            Expression.Constant(pos.Column))
            let exCatch = Expression.Catch(exParam, Expression.Throw(exNewRte, body.Type))

            Expression.TryCatch(body, rteCatch, rtePassCatch, exCatch) :> Expression

    // Cached MethodInfo for type hint coercion
    let private coerceToTypeMethod = typeof<JyroValue>.GetMethod("CoerceToType")

    /// Compile a variable declaration
    let private compileVarDecl (ctx: CompilationContext) (name: string) (typeHint: JyroType option) (init: Expr option) : Expression * CompilationContext =
        let varExpr = Expression.Variable(typeof<JyroValue>, name)
        let ctx' = ctx.WithVariable(name, varExpr)
        let ctx' =
            match typeHint with
            | Some hint -> ctx'.WithVariableType(name, hint)
            | None -> ctx'
        let initExpr =
            match init with
            | Some expr -> compileExpr ctx' expr
            | None -> Expression.Constant(JyroNull.Instance, typeof<JyroValue>) :> Expression
        let finalInit =
            match typeHint with
            | Some hint when hint <> AnyType ->
                let targetType = jyroTypeToValueType hint
                Expression.Call(coerceToTypeMethod,
                    initExpr,
                    Expression.Constant(targetType, typeof<JyroValueType>),
                    Expression.Constant(name, typeof<string>)) :> Expression
            | _ -> initExpr
        let assignExpr = Expression.Assign(varExpr, finalInit) :> Expression
        (assignExpr, ctx')

    /// Compile an assignment
    let private compileAssignment (ctx: CompilationContext) (target: Expr) (op: AssignOp) (value: Expr) : Expression =
        let valueExpr = compileExpr ctx value
        match target with
        | Identifier(name, _) when name <> "Data" ->
            match ctx.Variables.TryFind(name) with
            | Some varExpr ->
                let finalValue =
                    match op with
                    | Assign -> valueExpr
                    | AddAssign ->
                        let binaryOp = Expression.Constant(Add, typeof<BinaryOp>)
                        Expression.Call(varExpr, typeof<JyroValue>.GetMethod("EvaluateBinary"), binaryOp, valueExpr) :> Expression
                    | SubtractAssign ->
                        let binaryOp = Expression.Constant(Subtract, typeof<BinaryOp>)
                        Expression.Call(varExpr, typeof<JyroValue>.GetMethod("EvaluateBinary"), binaryOp, valueExpr) :> Expression
                    | MultiplyAssign ->
                        let binaryOp = Expression.Constant(Multiply, typeof<BinaryOp>)
                        Expression.Call(varExpr, typeof<JyroValue>.GetMethod("EvaluateBinary"), binaryOp, valueExpr) :> Expression
                    | DivideAssign ->
                        let binaryOp = Expression.Constant(Divide, typeof<BinaryOp>)
                        Expression.Call(varExpr, typeof<JyroValue>.GetMethod("EvaluateBinary"), binaryOp, valueExpr) :> Expression
                    | ModuloAssign ->
                        let binaryOp = Expression.Constant(Modulo, typeof<BinaryOp>)
                        Expression.Call(varExpr, typeof<JyroValue>.GetMethod("EvaluateBinary"), binaryOp, valueExpr) :> Expression
                let checkedValue =
                    match ctx.VariableTypes.TryFind(name) with
                    | Some hint when hint <> AnyType ->
                        let targetType = jyroTypeToValueType hint
                        Expression.Call(coerceToTypeMethod,
                            finalValue,
                            Expression.Constant(targetType, typeof<JyroValueType>),
                            Expression.Constant(name, typeof<string>)) :> Expression
                    | _ -> finalValue
                Expression.Assign(varExpr, checkedValue) :> Expression
            | None -> failwithf "Undefined variable: %s" name
        | PropertyAccess(obj, prop, _) ->
            let objExpr = compileExpr ctx obj
            let propConst = Expression.Constant(prop, typeof<string>)
            let finalValue =
                match op with
                | Assign -> valueExpr
                | _ ->
                    let currentValue = Expression.Call(objExpr, typeof<JyroValue>.GetMethod("GetProperty"), propConst) :> Expression
                    let binaryOp =
                        match op with
                        | AddAssign -> Expression.Constant(Add, typeof<BinaryOp>)
                        | SubtractAssign -> Expression.Constant(Subtract, typeof<BinaryOp>)
                        | MultiplyAssign -> Expression.Constant(Multiply, typeof<BinaryOp>)
                        | DivideAssign -> Expression.Constant(Divide, typeof<BinaryOp>)
                        | ModuloAssign -> Expression.Constant(Modulo, typeof<BinaryOp>)
                        | _ -> failwith "Unexpected assign op"
                    Expression.Call(currentValue, typeof<JyroValue>.GetMethod("EvaluateBinary"), binaryOp, valueExpr) :> Expression
            Expression.Call(objExpr, typeof<JyroValue>.GetMethod("SetProperty"), propConst, finalValue) :> Expression
        | IndexAccess(obj, index, _) ->
            let objExpr = compileExpr ctx obj
            let indexExpr = compileExpr ctx index
            let finalValue =
                match op with
                | Assign -> valueExpr
                | _ ->
                    let currentValue = Expression.Call(objExpr, typeof<JyroValue>.GetMethod("GetIndex"), indexExpr) :> Expression
                    let binaryOp =
                        match op with
                        | AddAssign -> Expression.Constant(Add, typeof<BinaryOp>)
                        | SubtractAssign -> Expression.Constant(Subtract, typeof<BinaryOp>)
                        | MultiplyAssign -> Expression.Constant(Multiply, typeof<BinaryOp>)
                        | DivideAssign -> Expression.Constant(Divide, typeof<BinaryOp>)
                        | ModuloAssign -> Expression.Constant(Modulo, typeof<BinaryOp>)
                        | _ -> failwith "Unexpected assign op"
                    Expression.Call(currentValue, typeof<JyroValue>.GetMethod("EvaluateBinary"), binaryOp, valueExpr) :> Expression
            Expression.Call(objExpr, typeof<JyroValue>.GetMethod("SetIndex"), indexExpr, finalValue) :> Expression
        | _ -> failwith "Invalid assignment target"

    /// Compile an if statement
    let rec private compileIf (ctx: CompilationContext) (cond: Expr) (thenBlock: Stmt list) (elseIfs: (Expr * Stmt list) list) (elseBlock: Stmt list option) : Expression =
        let condExpr = compileExpr ctx cond
        let testExpr = Expression.Call(condExpr, typeof<JyroValue>.GetMethod("ToBooleanTruthiness"))
        let thenExpr = compileBlock ctx thenBlock
        let elseExpr =
            match elseIfs, elseBlock with
            | [], None -> Expression.Empty() :> Expression
            | [], Some stmts -> compileBlock ctx stmts
            | (c, b) :: rest, _ -> compileIf ctx c b rest elseBlock
        Expression.IfThenElse(testExpr, thenExpr, elseExpr) :> Expression

    /// Compile a switch statement as a chain of if/else-if with equality checks
    and private compileSwitch (ctx: CompilationContext) (expr: Expr) (cases: Mesch.Jyro.SwitchCase list) (defaultCase: Stmt list option) : Expression =
        let switchExpr = compileExpr ctx expr
        let switchVar = Expression.Variable(typeof<JyroValue>, "switchVal")
        let assignSwitch = Expression.Assign(switchVar, switchExpr) :> Expression
        let equalsMethod = typeof<JyroValue>.GetMethod("EqualsValue")
        let rec buildCases (remaining: Mesch.Jyro.SwitchCase list) : Expression =
            match remaining with
            | [] ->
                match defaultCase with
                | Some stmts -> compileBlock ctx stmts
                | None -> Expression.Empty() :> Expression
            | case :: rest ->
                let testExpr =
                    case.Values
                    |> List.map (fun v -> Expression.Call(switchVar, equalsMethod, compileExpr ctx v) :> Expression)
                    |> List.reduce (fun a b -> Expression.OrElse(a, b) :> Expression)
                let bodyExpr = compileBlock ctx case.Body
                let elseExpr = buildCases rest
                Expression.IfThenElse(testExpr, bodyExpr, elseExpr) :> Expression
        let chain = buildCases cases
        Expression.Block(typeof<System.Void>, [| switchVar |], [| assignSwitch; chain |]) :> Expression

    /// Compile a while loop with iteration counting
    and private compileWhile (ctx: CompilationContext) (cond: Expr) (body: Stmt list) : Expression =
        let breakLabel = Expression.Label("break")
        let continueLabel = Expression.Label("continue")
        let ctx' = ctx.WithLoopLabels(breakLabel, continueLabel)
        let condExpr = compileExpr ctx' cond
        let testExpr = Expression.Call(condExpr, typeof<JyroValue>.GetMethod("ToBooleanTruthiness"))
        let bodyExpr = compileBlock ctx' body

        // Loop body with iteration check at the top of each iteration
        let checkedBody =
            Expression.IfThenElse(
                testExpr,
                Expression.Block(
                    Expression.Call(ctx.ContextParam, checkLoopMethod),
                    bodyExpr),
                Expression.Break(breakLabel))

        Expression.Loop(checkedBody, breakLabel, continueLabel) :> Expression

    /// Compile a range-based for loop with iteration counting
    and private compileFor (ctx: CompilationContext) (varName: string) (startExpr: Expr) (endExpr: Expr) (stepExpr: Expr option) (direction: ForDirection) (body: Stmt list) : Expression * CompilationContext =
        let breakLabel = Expression.Label("break")
        let continueLabel = Expression.Label("continue")

        // Create loop variable, end bound, and step variables
        let loopVar = Expression.Variable(typeof<JyroValue>, varName)
        let endVar = Expression.Variable(typeof<JyroValue>, "__end")
        let stepVar = Expression.Variable(typeof<JyroValue>, "__step")

        // Evaluate start/end/step once at loop entry (using outer context)
        let startCompiled = compileExpr ctx startExpr
        let endCompiled = compileExpr ctx endExpr
        let stepCompiled =
            match stepExpr with
            | Some expr -> compileExpr ctx expr
            | None -> Expression.Constant(JyroNumber(1.0) :> JyroValue, typeof<JyroValue>) :> Expression

        let initStart = Expression.Assign(loopVar, startCompiled) :> Expression
        let initEnd = Expression.Assign(endVar, endCompiled) :> Expression
        let initStep = Expression.Assign(stepVar, stepCompiled) :> Expression

        // Validate step is a positive integer - non-integer steps cause float drift, step <= 0 causes infinite loops
        let zeroValue = Expression.Constant(JyroNumber(0.0) :> JyroValue, typeof<JyroValue>) :> Expression
        let leOp = Expression.Constant(LessThanOrEqual, typeof<BinaryOp>)
        let stepLeZero = Expression.Call(stepVar, typeof<JyroValue>.GetMethod("EvaluateBinary"), leOp, zeroValue)
        let stepNotPositive = Expression.Call(stepLeZero, typeof<JyroValue>.GetMethod("ToBooleanTruthiness"))
        let stepAsNumber = Expression.TypeAs(stepVar, typeof<JyroNumber>)
        let stepNotInteger = Expression.OrElse(
            Expression.Equal(stepAsNumber, Expression.Constant(null, typeof<JyroNumber>)),
            Expression.Not(Expression.Property(stepAsNumber, "IsInteger")))
        let stepInvalid = Expression.OrElse(stepNotPositive, stepNotInteger)
        let stepErrorArgs = Expression.NewArrayInit(typeof<obj>,
            Expression.Constant("for(step)" :> obj, typeof<obj>),
            Expression.Constant("a positive integer step value" :> obj, typeof<obj>),
            Expression.Convert(stepVar, typeof<obj>))
        let stepErrorTemplate = MessageTemplates.get MessageCode.NonNegativeIntegerRequired
        let stepErrorMessage = Expression.Call(
            typeof<String>.GetMethod("Format", [| typeof<string>; typeof<obj[]> |]),
            Expression.Constant(stepErrorTemplate, typeof<string>),
            stepErrorArgs)
        let stepError = Expression.New(
            typeof<JyroRuntimeException>.GetConstructor([| typeof<MessageCode>; typeof<string>; typeof<obj[]> |]),
            Expression.Constant(MessageCode.NonNegativeIntegerRequired, typeof<MessageCode>),
            stepErrorMessage,
            stepErrorArgs)
        let stepCheck = Expression.IfThen(stepInvalid, Expression.Throw(stepError)) :> Expression

        // Set up context with loop variable and labels
        let ctx' = ctx.WithVariable(varName, loopVar).WithLoopLabels(breakLabel, continueLabel)

        // Condition: loopVar < endVar (ascending) or loopVar > endVar (descending)
        let comparisonOp =
            match direction with
            | Ascending -> Expression.Constant(LessThan, typeof<BinaryOp>)
            | Descending -> Expression.Constant(GreaterThan, typeof<BinaryOp>)
        let condResult = Expression.Call(loopVar, typeof<JyroValue>.GetMethod("EvaluateBinary"), comparisonOp, endVar)
        let testExpr = Expression.Call(condResult, typeof<JyroValue>.GetMethod("ToBooleanTruthiness"))

        // Compile loop body
        let bodyExpr = compileBlock ctx' body

        // Update: loopVar = loopVar + stepVar (ascending) or loopVar - stepVar (descending)
        let updateOp =
            match direction with
            | Ascending -> Expression.Constant(Add, typeof<BinaryOp>)
            | Descending -> Expression.Constant(Subtract, typeof<BinaryOp>)
        let updateExpr =
            Expression.Assign(
                loopVar,
                Expression.Call(loopVar, typeof<JyroValue>.GetMethod("EvaluateBinary"), updateOp, stepVar)) :> Expression

        // Loop body: if(!cond) break; checkLoop(); body; continueLabel; update
        // Continue label is placed before update so 'continue' still advances the counter
        let loopBody =
            Expression.IfThenElse(
                testExpr,
                Expression.Block(
                    [| Expression.Call(ctx.ContextParam, checkLoopMethod) :> Expression
                       bodyExpr
                       Expression.Label(continueLabel) :> Expression
                       updateExpr |]),
                Expression.Break(breakLabel))

        let loopExpr = Expression.Block(
            [| loopVar; endVar; stepVar |],
            [| initStart; initEnd; initStep; stepCheck
               Expression.Loop(loopBody, breakLabel) :> Expression |]) :> Expression

        // Return the original context - loop variable is scoped to the for block
        (loopExpr, ctx)

    /// Compile a foreach loop with iteration counting
    and private compileForEach (ctx: CompilationContext) (varName: string) (collection: Expr) (body: Stmt list) : Expression =
        let breakLabel = Expression.Label("break")
        let continueLabel = Expression.Label("continue")
        let varParam = Expression.Variable(typeof<JyroValue>, varName)
        let ctx' = ctx.WithVariable(varName, varParam).WithLoopLabels(breakLabel, continueLabel)

        let collectionExpr = compileExpr ctx collection
        let iterableExpr = Expression.Call(collectionExpr, typeof<JyroValue>.GetMethod("ToIterable"))
        let enumeratorVar = Expression.Variable(typeof<System.Collections.Generic.IEnumerator<JyroValue>>, "enumerator")
        let getEnumeratorExpr = Expression.Call(iterableExpr, typeof<System.Collections.Generic.IEnumerable<JyroValue>>.GetMethod("GetEnumerator"))

        let moveNextExpr = Expression.Call(enumeratorVar, typeof<System.Collections.IEnumerator>.GetMethod("MoveNext"))
        let currentProp = typeof<System.Collections.Generic.IEnumerator<JyroValue>>.GetProperty("Current")
        let currentExpr = Expression.Property(enumeratorVar, currentProp)
        let assignCurrent = Expression.Assign(varParam, currentExpr)
        let bodyExpr = compileBlock ctx' body

        // Loop body with iteration check after MoveNext succeeds
        let checkedBody =
            Expression.IfThenElse(
                moveNextExpr,
                Expression.Block(
                    [| Expression.Call(ctx.ContextParam, checkLoopMethod) :> Expression
                       assignCurrent :> Expression
                       bodyExpr |]),
                Expression.Break(breakLabel))

        Expression.Block(
            [| enumeratorVar; varParam |],
            [| Expression.Assign(enumeratorVar, getEnumeratorExpr) :> Expression
               Expression.Loop(checkedBody, breakLabel, continueLabel) :> Expression |]) :> Expression

    /// Compile a match statement as a chain of if/else-if on _variant tag
    and private compileMatch (ctx: CompilationContext) (expr: Expr) (cases: MatchCase list) : Expression =
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

        let rec buildCases (remaining: MatchCase list) : Expression =
            match remaining with
            | [] -> Expression.Empty() :> Expression
            | case :: rest ->
                // Compare variantTag == case.VariantName
                let testExpr =
                    Expression.Equal(
                        variantTagVar,
                        Expression.Constant(case.VariantName, typeof<string>)) :> Expression

                // Destructure fields: look up variant's field names from ctx.UnionDefinitions
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

                // Compile case body with bindings in scope
                let bodyExpr = compileBlock caseCtx case.Body
                let caseBlock =
                    if bindingVars.Count > 0 then
                        let allExprs = ResizeArray<Expression>()
                        allExprs.AddRange(bindingInits)
                        allExprs.Add(bodyExpr)
                        Expression.Block(bindingVars, allExprs) :> Expression
                    else
                        bodyExpr

                let elseExpr = buildCases rest
                Expression.IfThenElse(testExpr, caseBlock, elseExpr) :> Expression

        let chain = buildCases cases
        Expression.Block(typeof<System.Void>, [| matchVar; variantTagVar |], [| assignMatch; assignTag; chain |]) :> Expression

    /// Compile a block of statements
    and compileBlock (ctx: CompilationContext) (stmts: Stmt list) : Expression =
        let variables = ResizeArray<ParameterExpression>()
        let addedVars = System.Collections.Generic.HashSet<string>()
        let expressions = ResizeArray<Expression>()
        let mutable ctx' = ctx

        for stmt in stmts do
            let (expr, newCtx) = compileStmt ctx' stmt
            // Collect new variables (including shadows of outer-scope variables)
            for kvp in newCtx.Variables do
                let isNewName = not (ctx.Variables.ContainsKey(kvp.Key))
                let isShadow =
                    match ctx.Variables.TryFind(kvp.Key) with
                    | Some existing -> not (Object.ReferenceEquals(existing, kvp.Value))
                    | None -> false
                if (isNewName || isShadow) && addedVars.Add(kvp.Key) then
                    variables.Add(kvp.Value)
            ctx' <- newCtx
            expressions.Add(expr)

        if expressions.Count = 0 then
            Expression.Empty() :> Expression
        else
            Expression.Block(variables, expressions) :> Expression

    /// Compile a single statement with resource checks and location tracking
    and compileStmt (ctx: CompilationContext) (stmt: Stmt) : Expression * CompilationContext =
        let pos = stmt.Position
        let (compiled, ctx') =
            match stmt with
            | VarDecl(name, typeHint, init, _) ->
                let (expr, ctx') = compileVarDecl ctx name typeHint init
                (withStatementCheck ctx expr, ctx')
            | Assignment(target, op, value, _) ->
                (withStatementCheck ctx (compileAssignment ctx target op value), ctx)
            | If(cond, thenBlock, elseIfs, elseBlock, _) ->
                (withStatementCheck ctx (compileIf ctx cond thenBlock elseIfs elseBlock), ctx)
            | While(cond, body, _) ->
                (withStatementCheck ctx (compileWhile ctx cond body), ctx)
            | ForEach(varName, collection, body, _) ->
                (withStatementCheck ctx (compileForEach ctx varName collection body), ctx)
            | For(varName, startExpr, endExpr, stepExpr, direction, body, _) ->
                let (expr, ctx') = compileFor ctx varName startExpr endExpr stepExpr direction body
                (withStatementCheck ctx expr, ctx')
            | Switch(expr, cases, defaultCase, _) ->
                (withStatementCheck ctx (compileSwitch ctx expr cases defaultCase), ctx)
            | Return(valueOpt, _) ->
                // Return from a function - returns the expression value (or null if bare return)
                let returnValue =
                    match valueOpt with
                    | Some expr -> compileExpr ctx expr
                    | None -> Expression.Constant(JyroNull.Instance, typeof<JyroValue>) :> Expression
                match ctx.ReturnLabel with
                | Some label ->
                    (withStatementCheck ctx (Expression.Return(label, returnValue) :> Expression), ctx)
                | None -> failwith "Return without return label"
            | Exit(messageOpt, _) ->
                // Exit - clean script termination
                let setMessageExpr =
                    match messageOpt with
                    | Some expr ->
                        let compiled = compileExpr ctx expr
                        let msgStr = Expression.Call(compiled, typeof<JyroValue>.GetMethod("ToStringValue")) :> Expression
                        Some (Expression.Call(ctx.ContextParam, setReturnMessageMethod, msgStr) :> Expression)
                    | None -> None
                if ctx.InFunction then
                    // Inside a function, exit must escape the entire call stack.
                    // Throw a JyroRuntimeException with ScriptReturn code - execute() handles this as success.
                    let messageExpr =
                        match messageOpt with
                        | Some expr ->
                            let compiled = compileExpr ctx expr
                            Expression.Call(compiled, typeof<JyroValue>.GetMethod("ToStringValue")) :> Expression
                        | None ->
                            Expression.Constant("", typeof<string>) :> Expression
                    let rteNew = Expression.New(
                        typeof<JyroRuntimeException>.GetConstructor([| typeof<MessageCode>; typeof<string> |]),
                        Expression.Constant(MessageCode.ScriptReturn, typeof<MessageCode>),
                        messageExpr)
                    let throwExpr = Expression.Throw(rteNew, typeof<System.Void>) :> Expression
                    let block =
                        match setMessageExpr with
                        | Some setMsg -> Expression.Block(setMsg, throwExpr) :> Expression
                        | None -> throwExpr
                    (withStatementCheck ctx block, ctx)
                else
                    // At top level, jump to the script return label
                    match ctx.ReturnLabel with
                    | Some label ->
                        let returnExpr = Expression.Return(label, ctx.DataParam :> Expression) :> Expression
                        let block =
                            match setMessageExpr with
                            | Some setMsg -> Expression.Block(setMsg, returnExpr) :> Expression
                            | None -> returnExpr
                        (withStatementCheck ctx block, ctx)
                    | None ->
                        let block =
                            match setMessageExpr with
                            | Some setMsg -> Expression.Block(setMsg, ctx.DataParam :> Expression) :> Expression
                            | None -> ctx.DataParam :> Expression
                        (withStatementCheck ctx block, ctx)
            | FuncDef _ ->
                // No-op at script level - function bodies are compiled separately
                (Expression.Empty() :> Expression, ctx)
            | UnionDef _ ->
                // No-op at script level - union definitions are processed by the linker
                (Expression.Empty() :> Expression, ctx)
            | Match(expr, cases, _) ->
                (withStatementCheck ctx (compileMatch ctx expr cases), ctx)
            | Fail(messageOpt, _) ->
                let messageExpr =
                    match messageOpt with
                    | Some expr ->
                        let compiled = compileExpr ctx expr
                        Expression.Call(compiled, typeof<JyroValue>.GetMethod("ToStringValue")) :> Expression
                    | None ->
                        Expression.Constant("Script execution failed", typeof<string>) :> Expression
                let setMsgExpr = Expression.Call(ctx.ContextParam, setReturnMessageMethod, messageExpr) :> Expression
                let rteNew = Expression.New(
                    typeof<JyroRuntimeException>.GetConstructor([| typeof<MessageCode>; typeof<string> |]),
                    Expression.Constant(MessageCode.ScriptFailure, typeof<MessageCode>),
                    messageExpr)
                let throwExpr = Expression.Throw(rteNew, typeof<System.Void>) :> Expression
                (withStatementCheck ctx (Expression.Block(setMsgExpr, throwExpr) :> Expression), ctx)
            | Break _ ->
                match ctx.BreakLabel with
                | Some label -> (Expression.Break(label) :> Expression, ctx)
                | None -> failwith "Break outside of loop"
            | Continue _ ->
                match ctx.ContinueLabel with
                | Some label -> (Expression.Continue(label) :> Expression, ctx)
                | None -> failwith "Continue outside of loop"
            | ExprStmt(expr, _) ->
                (withStatementCheck ctx (compileExpr ctx expr), ctx)
        (withLocationTracking compiled pos, ctx')
