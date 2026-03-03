namespace Mesch.Jyro


/// Semantic validation for Jyro programs
module Validator =
    /// Variable scope tracking
    type Scope =
        { Variables: Set<string>
          Parent: Scope option
          InLoop: bool }

        static member Empty = { Variables = Set.empty; Parent = None; InLoop = false }

        member this.WithVariable(name: string) =
            { this with Variables = this.Variables.Add(name) }

        member this.Append() =
            { Variables = Set.empty; Parent = Some this; InLoop = this.InLoop }

        member this.EnterLoop() =
            { this with InLoop = true }

        member this.IsDeclared(name: string) : bool =
            if this.Variables.Contains(name) then true
            elif this.Parent.IsSome then this.Parent.Value.IsDeclared(name)
            else false

    /// Validation context
    type ValidationContext =
        { Scope: Scope
          Messages: DiagnosticMessage list
          DefinedFunctions: Set<string>
          DefinedUnions: Map<string, UnionVariant list>
          VariantToUnion: Map<string, string>
          InFunction: bool
          NestingDepth: int }

        static member Empty =
            { Scope = Scope.Empty
              Messages = []
              DefinedFunctions = Set.ofList [ "Data" ]
              DefinedUnions = Map.empty
              VariantToUnion = Map.empty
              InFunction = false
              NestingDepth = 0 }

        member this.AddError(code: MessageCode, message: string, args: obj[], location: SourceLocation) =
            let msg = DiagnosticMessage.Error(code, message, args = args, location = location)
            { this with Messages = msg :: this.Messages }

        member this.AddWarning(code: MessageCode, message: string, args: obj[], location: SourceLocation) =
            let msg = DiagnosticMessage.Warning(code, message, args = args, location = location)
            { this with Messages = msg :: this.Messages }

    /// Validate an expression
    let rec validateExpr (ctx: ValidationContext) (expr: Expr) : ValidationContext =
        match expr with
        | Identifier(name, pos) when name = "Data" && ctx.InFunction ->
            let loc = SourceLocation.Create(pos.Line, pos.Column)
            ctx.AddError(MessageCode.DataAccessInFunction,
                "Cannot access Data inside a function. Pass it as a parameter instead.",
                Array.empty, loc)
        | Identifier(name, pos) when name <> "Data" && not (ctx.Scope.IsDeclared(name)) ->
            let loc = SourceLocation.Create(pos.Line, pos.Column)
            let msg = sprintf "Undeclared variable '%s'" name
            ctx.AddError(MessageCode.UndeclaredVariable, msg, [| box name |], loc)
        | Binary(left, _, right, _) ->
            ctx |> validateExpr <| left |> validateExpr <| right
        | Unary(_, operand, _) ->
            ctx |> validateExpr <| operand
        | Ternary(cond, thenExpr, elseExpr, _) ->
            ctx |> validateExpr <| cond |> validateExpr <| thenExpr |> validateExpr <| elseExpr
        | Call(_, args, _) ->
            args |> List.fold validateExpr ctx
        | PropertyAccess(target, _, _) ->
            ctx |> validateExpr <| target
        | IndexAccess(target, index, _) ->
            ctx |> validateExpr <| target |> validateExpr <| index
        | ObjectLiteral(props, _) ->
            props |> List.fold (fun c (_, e) -> validateExpr c e) ctx
        | ArrayLiteral(elems, _) ->
            elems |> List.fold validateExpr ctx
        | Lambda(params', body, _) ->
            let innerCtx = { ctx with Scope = params' |> List.fold (fun s p -> s.WithVariable(p)) (ctx.Scope.Append()) }
            let validated = validateExpr innerCtx body
            { ctx with Messages = validated.Messages }
        | TypeCheck(e, _, _, _) ->
            ctx |> validateExpr <| e
        | IncrementDecrement(e, _, _, _) ->
            ctx |> validateExpr <| e
        | MatchExpr(expr, cases, pos) ->
            let ctx' = validateExpr ctx expr
            let loc = SourceLocation.Create(pos.Line, pos.Column)
            // Determine which union from the first case
            let unionNameOpt =
                match cases with
                | mc :: _ -> ctx'.VariantToUnion.TryFind(mc.VariantName)
                | [] -> None
            // Validate each case
            let seenCases = System.Collections.Generic.HashSet<string>()
            let ctx' =
                cases |> List.fold (fun (c: ValidationContext) (mc: MatchExprCase) ->
                    let caseLoc = SourceLocation.Create(mc.Pos.Line, mc.Pos.Column)
                    if not (seenCases.Add(mc.VariantName)) then
                        c.AddError(MessageCode.MatchDuplicateCase,
                            sprintf "Duplicate case '%s' in match expression" mc.VariantName,
                            [| box mc.VariantName |], caseLoc)
                    elif not (c.VariantToUnion.ContainsKey(mc.VariantName)) then
                        c.AddError(MessageCode.MatchUnknownVariant,
                            sprintf "Unknown variant '%s' in match case" mc.VariantName,
                            [| box mc.VariantName |], caseLoc)
                    else
                        let unionName = c.VariantToUnion.[mc.VariantName]
                        let variantDef = c.DefinedUnions.[unionName] |> List.find (fun v -> v.Name = mc.VariantName)
                        if mc.Bindings.Length <> variantDef.Fields.Length then
                            c.AddError(MessageCode.MatchBindingCountMismatch,
                                sprintf "Case '%s' has %d binding(s) but variant '%s' has %d field(s)" mc.VariantName mc.Bindings.Length mc.VariantName variantDef.Fields.Length,
                                [| box mc.VariantName; box mc.Bindings.Length; box variantDef.Fields.Length |], caseLoc)
                        else
                            // Validate body expression in a scope with bindings declared
                            let bodyScope = mc.Bindings |> List.fold (fun (s: Scope) b -> s.WithVariable(b)) (c.Scope.Append())
                            let bodyCtx = validateExpr { c with Scope = bodyScope } mc.Body
                            { bodyCtx with Scope = c.Scope }
                ) ctx'
            // Exhaustiveness check
            match unionNameOpt with
            | Some unionName ->
                let allVariants = ctx'.DefinedUnions.[unionName] |> List.map (fun v -> v.Name) |> Set.ofList
                let coveredVariants = cases |> List.map (fun c -> c.VariantName) |> Set.ofList
                let missing = Set.difference allVariants coveredVariants
                if not missing.IsEmpty then
                    let missingStr = missing |> Set.toList |> String.concat ", "
                    ctx'.AddError(MessageCode.MatchNotExhaustive,
                        sprintf "Match on union '%s' is not exhaustive; missing case(s): %s" unionName missingStr,
                        [| box unionName; box missingStr |], loc)
                else ctx'
            | None -> ctx'
        | _ -> ctx

    /// Validate a statement
    let rec validateStmt (ctx: ValidationContext) (stmt: Stmt) : ValidationContext =
        match stmt with
        | VarDecl(name, typeHint, init, pos) ->
            let ctx' =
                match init with
                | Some expr -> validateExpr ctx expr
                | None -> ctx
            let ctx' =
                match typeHint, init with
                | Some hint, None when hint <> AnyType ->
                    let loc = SourceLocation.Create(pos.Line, pos.Column)
                    let msg = sprintf "Typed variable '%s' must have an initializer" name
                    ctx'.AddError(MessageCode.TypeMismatch, msg, [| box msg |], loc)
                | _ -> ctx'
            if ctx'.Scope.Variables.Contains(name) then
                let loc = SourceLocation.Create(pos.Line, pos.Column)
                let msg = sprintf "Variable '%s' is already declared" name
                ctx'.AddError(MessageCode.VariableAlreadyDeclared, msg, [| box name |], loc)
            else
                { ctx' with Scope = ctx'.Scope.WithVariable(name) }
        | Assignment(target, _, value, pos) ->
            let ctx' = validateExpr ctx target |> validateExpr <| value
            if not (Ast.isAssignmentTarget target) then
                let loc = SourceLocation.Create(pos.Line, pos.Column)
                ctx'.AddError(MessageCode.InvalidAssignmentTarget, "Invalid assignment target", Array.empty, loc)
            else
                ctx'
        | If(cond, thenBlock, elseIfs, elseBlock, _) ->
            let ctx' = validateExpr ctx cond
            let nested = { ctx' with NestingDepth = ctx'.NestingDepth + 1 }
            let thenCtx = thenBlock |> List.fold validateStmt { nested with Scope = ctx'.Scope.Append() }
            let ctx'' = { thenCtx with Scope = ctx'.Scope; NestingDepth = ctx'.NestingDepth }
            let ctx''' = elseIfs |> List.fold (fun c (e, stmts) ->
                let c' = validateExpr c e
                let nestedC = { c' with NestingDepth = c'.NestingDepth + 1 }
                let bodyCtx = stmts |> List.fold validateStmt { nestedC with Scope = c'.Scope.Append() }
                { bodyCtx with Scope = c'.Scope; NestingDepth = c'.NestingDepth }) ctx''
            match elseBlock with
            | Some stmts ->
                let nestedElse = { ctx''' with NestingDepth = ctx'''.NestingDepth + 1 }
                let bodyCtx = stmts |> List.fold validateStmt { nestedElse with Scope = ctx'''.Scope.Append() }
                { bodyCtx with Scope = ctx'''.Scope; NestingDepth = ctx'''.NestingDepth }
            | None -> ctx'''
        | While(cond, body, _) ->
            let ctx' = validateExpr ctx cond
            let loopCtx = { ctx' with Scope = ctx'.Scope.Append().EnterLoop(); NestingDepth = ctx'.NestingDepth + 1 }
            let bodyCtx = body |> List.fold validateStmt loopCtx
            { bodyCtx with Scope = ctx'.Scope; NestingDepth = ctx'.NestingDepth }
        | ForEach(varName, collection, body, _) ->
            let ctx' = validateExpr ctx collection
            let ctx' =
                match collection with
                | Literal(v, pos) when v.ValueType = JyroValueType.Null
                                    || v.ValueType = JyroValueType.Number
                                    || v.ValueType = JyroValueType.Boolean ->
                    let loc = SourceLocation.Create(pos.Line, pos.Column)
                    let msg = sprintf "Value of type %A is not iterable" v.ValueType
                    ctx'.AddError(MessageCode.NotIterableLiteral, msg, [| box v.ValueType |], loc)
                | _ -> ctx'
            let loopScope = ctx'.Scope.Append().EnterLoop().WithVariable(varName)
            let loopCtx = { ctx' with Scope = loopScope; NestingDepth = ctx'.NestingDepth + 1 }
            let bodyCtx = body |> List.fold validateStmt loopCtx
            { bodyCtx with Scope = ctx'.Scope; NestingDepth = ctx'.NestingDepth }
        | For(varName, startExpr, endExpr, stepExpr, _, body, _) ->
            let ctx' = validateExpr ctx startExpr
            let ctx' = validateExpr ctx' endExpr
            let ctx' = match stepExpr with Some e -> validateExpr ctx' e | None -> ctx'
            let loopScope = ctx'.Scope.Append().EnterLoop().WithVariable(varName)
            let bodyCtx = body |> List.fold validateStmt { ctx' with Scope = loopScope; NestingDepth = ctx'.NestingDepth + 1 }
            { bodyCtx with Scope = ctx'.Scope; NestingDepth = ctx'.NestingDepth }
        | Switch(expr, cases, defaultCase, _) ->
            let ctx' = validateExpr ctx expr
            let ctx'' = cases |> List.fold (fun c case ->
                let c' = case.Values |> List.fold validateExpr c
                let nestedC = { c' with NestingDepth = c'.NestingDepth + 1 }
                let bodyCtx = case.Body |> List.fold validateStmt { nestedC with Scope = c'.Scope.Append() }
                { bodyCtx with Scope = c'.Scope; NestingDepth = c'.NestingDepth }) ctx'
            match defaultCase with
            | Some stmts ->
                let nestedDef = { ctx'' with NestingDepth = ctx''.NestingDepth + 1 }
                let bodyCtx = stmts |> List.fold validateStmt { nestedDef with Scope = ctx''.Scope.Append() }
                { bodyCtx with Scope = ctx''.Scope; NestingDepth = ctx''.NestingDepth }
            | None -> ctx''
        | FuncDef(name, parameters, body, pos) ->
            let loc = SourceLocation.Create(pos.Line, pos.Column)
            // Functions must be at the top level (not nested inside control flow)
            let ctx' =
                if ctx.NestingDepth > 0 then
                    ctx.AddError(MessageCode.FunctionNotAtTopLevel,
                        sprintf "Function '%s' must be declared at the top level" name,
                        [| box name |], loc)
                else ctx
            // No nested functions
            let ctx' =
                if ctx.InFunction then
                    ctx'.AddError(MessageCode.NestedFunctionDeclaration,
                        "Function declarations cannot be nested inside other functions",
                        Array.empty, loc)
                else ctx'
            // Check for duplicate parameter names
            let ctx' =
                parameters |> List.fold (fun (c: ValidationContext, seen: Set<string>) (pName, _) ->
                    if seen.Contains(pName) then
                        let c' = c.AddError(MessageCode.DuplicateParameterName,
                            sprintf "Duplicate parameter '%s' in function '%s'" pName name,
                            [| box pName; box name |], loc)
                        (c', seen)
                    elif pName = "Data" then
                        let c' = c.AddError(MessageCode.ReservedParameterName,
                            sprintf "Cannot use reserved name '%s' as a parameter in function '%s'" pName name,
                            [| box pName; box name |], loc)
                        (c', seen.Add(pName))
                    else
                        (c, seen.Add(pName))
                ) (ctx', Set.empty) |> fst
            // Track function name for duplicate detection across the program
            let ctx' =
                if ctx'.DefinedFunctions.Contains(name) then
                    ctx'.AddError(MessageCode.VariableAlreadyDeclared,
                        sprintf "Function '%s' is already defined" name,
                        [| box name |], loc)
                else
                    { ctx' with DefinedFunctions = ctx'.DefinedFunctions.Add(name) }
            // Validate function body in a new scope with parameters declared, InFunction = true
            let funcScope =
                parameters |> List.fold (fun (s: Scope) (pName, _) -> s.WithVariable(pName)) (Scope.Empty)
            let funcCtx =
                { ctx' with
                    Scope = funcScope
                    InFunction = true
                    NestingDepth = 0 }
            let bodyCtx = body |> List.fold validateStmt funcCtx
            // Restore outer context with accumulated messages
            { ctx' with Messages = bodyCtx.Messages }
        | Return(valueOpt, pos) ->
            if not ctx.InFunction then
                let loc = SourceLocation.Create(pos.Line, pos.Column)
                let ctx' = ctx.AddError(MessageCode.ReturnOutsideFunction,
                    "'return' can only be used inside a function. Use 'exit' to terminate the script.",
                    Array.empty, loc)
                match valueOpt with
                | Some expr -> validateExpr ctx' expr
                | None -> ctx'
            else
                match valueOpt with
                | Some expr -> validateExpr ctx expr
                | None -> ctx
        | Exit(valueOpt, _) ->
            match valueOpt with
            | Some expr -> validateExpr ctx expr
            | None -> ctx
        | Fail(msgOpt, _) ->
            match msgOpt with
            | Some expr -> validateExpr ctx expr
            | None -> ctx
        | Break(pos) ->
            if not ctx.Scope.InLoop then
                let loc = SourceLocation.Create(pos.Line, pos.Column)
                ctx.AddError(MessageCode.LoopStatementOutsideOfLoop, "Break statement outside of loop", [| box "Break" |], loc)
            else
                ctx
        | Continue(pos) ->
            if not ctx.Scope.InLoop then
                let loc = SourceLocation.Create(pos.Line, pos.Column)
                ctx.AddError(MessageCode.LoopStatementOutsideOfLoop, "Continue statement outside of loop", [| box "Continue" |], loc)
            else
                ctx
        | ExprStmt(expr, _) ->
            validateExpr ctx expr
        | UnionDef(name, _, pos) ->
            let loc = SourceLocation.Create(pos.Line, pos.Column)
            // Must be at top level
            let ctx' =
                if ctx.NestingDepth > 0 then
                    ctx.AddError(MessageCode.UnionNotAtTopLevel,
                        sprintf "Union '%s' must be declared at the top level" name,
                        [| box name |], loc)
                else ctx
            // Cannot be inside a function
            if ctx.InFunction then
                ctx'.AddError(MessageCode.UnionInsideFunction,
                    "Union declarations cannot be inside functions",
                    Array.empty, loc)
            else ctx'
        | Match(expr, cases, pos) ->
            let ctx' = validateExpr ctx expr
            let loc = SourceLocation.Create(pos.Line, pos.Column)
            // Determine which union from the first case
            let unionNameOpt =
                match cases with
                | mc :: _ -> ctx'.VariantToUnion.TryFind(mc.VariantName)
                | [] -> None
            // Validate each case
            let seenCases = System.Collections.Generic.HashSet<string>()
            let ctx' =
                cases |> List.fold (fun (c: ValidationContext) (mc: MatchCase) ->
                    let caseLoc = SourceLocation.Create(mc.Pos.Line, mc.Pos.Column)
                    if not (seenCases.Add(mc.VariantName)) then
                        c.AddError(MessageCode.MatchDuplicateCase,
                            sprintf "Duplicate case '%s' in match statement" mc.VariantName,
                            [| box mc.VariantName |], caseLoc)
                    elif not (c.VariantToUnion.ContainsKey(mc.VariantName)) then
                        c.AddError(MessageCode.MatchUnknownVariant,
                            sprintf "Unknown variant '%s' in match case" mc.VariantName,
                            [| box mc.VariantName |], caseLoc)
                    else
                        let unionName = c.VariantToUnion.[mc.VariantName]
                        let variantDef = c.DefinedUnions.[unionName] |> List.find (fun v -> v.Name = mc.VariantName)
                        if mc.Bindings.Length <> variantDef.Fields.Length then
                            c.AddError(MessageCode.MatchBindingCountMismatch,
                                sprintf "Case '%s' has %d binding(s) but variant '%s' has %d field(s)" mc.VariantName mc.Bindings.Length mc.VariantName variantDef.Fields.Length,
                                [| box mc.VariantName; box mc.Bindings.Length; box variantDef.Fields.Length |], caseLoc)
                        else
                            // Validate body in a scope with bindings declared
                            let bodyScope = mc.Bindings |> List.fold (fun (s: Scope) b -> s.WithVariable(b)) (c.Scope.Append())
                            let bodyCtx = mc.Body |> List.fold validateStmt { c with Scope = bodyScope; NestingDepth = c.NestingDepth + 1 }
                            { bodyCtx with Scope = c.Scope; NestingDepth = c.NestingDepth }
                ) ctx'
            // Exhaustiveness check
            match unionNameOpt with
            | Some unionName ->
                let allVariants = ctx'.DefinedUnions.[unionName] |> List.map (fun v -> v.Name) |> Set.ofList
                let coveredVariants = cases |> List.map (fun c -> c.VariantName) |> Set.ofList
                let missing = Set.difference allVariants coveredVariants
                if not missing.IsEmpty then
                    let missingStr = missing |> Set.toList |> String.concat ", "
                    ctx'.AddError(MessageCode.MatchNotExhaustive,
                        sprintf "Match on union '%s' is not exhaustive; missing case(s): %s" unionName missingStr,
                        [| box unionName; box missingStr |], loc)
                else ctx'
            | None -> ctx'

    /// Validate a program
    let validate (program: Program) : JyroResult<Program> =
        // Pre-pass: collect union declarations and check for cross-union duplicate variants
        let initialCtx =
            program.Statements |> List.fold (fun (c: ValidationContext) stmt ->
                match stmt with
                | UnionDef(name, variants, pos) ->
                    let loc = SourceLocation.Create(pos.Line, pos.Column)
                    let c' =
                        variants |> List.fold (fun (c2: ValidationContext) (v: UnionVariant) ->
                            match c2.VariantToUnion.TryFind(v.Name) with
                            | Some existingUnion ->
                                c2.AddError(MessageCode.DuplicateVariantName,
                                    sprintf "Variant '%s' is already defined in union '%s'" v.Name existingUnion,
                                    [| box v.Name; box existingUnion |], loc)
                            | None -> c2
                        ) c
                    { c' with
                        DefinedUnions = c'.DefinedUnions.Add(name, variants)
                        VariantToUnion = variants |> List.fold (fun m v -> m.Add(v.Name, name)) c'.VariantToUnion }
                | _ -> c
            ) ValidationContext.Empty
        let ctx = program.Statements |> List.fold validateStmt initialCtx
        let messages = ctx.Messages |> List.rev
        if messages |> List.exists (fun m -> m.Severity = MessageSeverity.Error) then
            JyroResult<Program>.Failure(messages)
        else
            { Value = Some program; Messages = messages; IsSuccess = true }
