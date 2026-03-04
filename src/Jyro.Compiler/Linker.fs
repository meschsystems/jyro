namespace Mesch.Jyro


/// Linker for resolving function references
module Linker =
    /// Linking context
    type LinkContext =
        { Functions: Map<string, IJyroFunction>
          HostFunctions: Set<string>
          ReferencedFunctions: Set<string>
          Messages: DiagnosticMessage list }

        static member Empty =
            { Functions = Map.empty
              HostFunctions = Set.empty
              ReferencedFunctions = Set.empty
              Messages = [] }

        member this.AddFunction(func: IJyroFunction) =
            { this with Functions = this.Functions.Add(func.Name, func) }

        member this.MarkReferenced(name: string) =
            { this with ReferencedFunctions = this.ReferencedFunctions.Add(name) }

        member this.AddError(code: MessageCode, message: string, args: obj[], location: SourceLocation) =
            let msg = DiagnosticMessage.Error(code, message, args = args, location = location)
            { this with Messages = msg :: this.Messages }

    /// Check function calls in an expression
    let rec private linkExpr (ctx: LinkContext) (expr: Expr) : LinkContext =
        match expr with
        | Call(name, callArgs, pos) ->
            let ctx' =
                match callArgs with
                | PositionalArgs exprs -> exprs |> List.fold linkExpr ctx
                | NamedArgs pairs -> pairs |> List.fold (fun c (_, expr) -> linkExpr c expr) ctx
            match ctx'.Functions.TryFind(name) with
            | Some func ->
                match callArgs with
                | PositionalArgs exprs ->
                    if not (func.Signature.ValidateArgCount(exprs.Length)) then
                        let loc = SourceLocation.Create(pos.Line, pos.Column)
                        if exprs.Length < func.Signature.MinArgs then
                            let msg = sprintf "Function '%s' requires at least %d arguments, but %d were provided" name func.Signature.MinArgs exprs.Length
                            let msgArgs = [| box name; box func.Signature.MinArgs; box exprs.Length |]
                            ctx'.AddError(MessageCode.TooFewArguments, msg, msgArgs, loc)
                        else
                            let msg = sprintf "Function '%s' accepts at most %d arguments, but %d were provided" name func.Signature.MaxArgs exprs.Length
                            let msgArgs = [| box name; box func.Signature.MaxArgs; box exprs.Length |]
                            ctx'.AddError(MessageCode.TooManyArguments, msg, msgArgs, loc)
                    else
                        ctx'.MarkReferenced(name)
                | NamedArgs pairs ->
                    let loc = SourceLocation.Create(pos.Line, pos.Column)
                    let paramNames = func.Signature.Parameters |> List.map (fun p -> p.Name) |> Set.ofList
                    let requiredParams = func.Signature.Parameters |> List.filter (fun p -> not p.IsOptional) |> List.map (fun p -> p.Name) |> Set.ofList
                    let providedNames = pairs |> List.map fst
                    let providedSet = providedNames |> Set.ofList
                    // Check for duplicate named args
                    let ctx'' =
                        providedNames
                        |> List.groupBy id
                        |> List.filter (fun (_, group) -> group.Length > 1)
                        |> List.fold (fun (c: LinkContext) (dupName, _) ->
                            let msg = sprintf "Duplicate named argument '%s' in call to '%s'" dupName name
                            c.AddError(MessageCode.DuplicateNamedArgument, msg, [| box dupName; box name |], loc)
                        ) ctx'
                    // Check for unknown parameter names
                    let ctx'' =
                        Set.difference providedSet paramNames
                        |> Set.fold (fun (c: LinkContext) unknownName ->
                            let available = func.Signature.Parameters |> List.map (fun p -> p.Name) |> String.concat ", "
                            let msg = sprintf "Unknown parameter '%s' in call to '%s'. Available parameters: %s" unknownName name available
                            c.AddError(MessageCode.UnknownNamedArgument, msg, [| box unknownName; box name; box available |], loc)
                        ) ctx''
                    // Check for missing required parameters
                    let ctx'' =
                        Set.difference requiredParams providedSet
                        |> Set.fold (fun (c: LinkContext) missingName ->
                            let msg = sprintf "Required parameter '%s' not provided in call to '%s'" missingName name
                            c.AddError(MessageCode.MissingRequiredNamedArgument, msg, [| box missingName; box name |], loc)
                        ) ctx''
                    ctx''.MarkReferenced(name)
            | None ->
                let loc = SourceLocation.Create(pos.Line, pos.Column)
                let msg = sprintf "Undefined function '%s'" name
                ctx'.AddError(MessageCode.UndefinedFunction, msg, [| box name |], loc)
        | Binary(left, _, right, _) ->
            ctx |> linkExpr <| left |> linkExpr <| right
        | Unary(_, operand, _) ->
            ctx |> linkExpr <| operand
        | Ternary(cond, thenExpr, elseExpr, _) ->
            ctx |> linkExpr <| cond |> linkExpr <| thenExpr |> linkExpr <| elseExpr
        | PropertyAccess(target, _, _) ->
            ctx |> linkExpr <| target
        | IndexAccess(target, index, _) ->
            ctx |> linkExpr <| target |> linkExpr <| index
        | ObjectLiteral(props, _) ->
            props |> List.fold (fun c (_, e) -> linkExpr c e) ctx
        | ArrayLiteral(elems, _) ->
            elems |> List.fold linkExpr ctx
        | Lambda(_, body, _) ->
            ctx |> linkExpr <| body
        | TypeCheck(e, _, _, _) ->
            ctx |> linkExpr <| e
        | IncrementDecrement(e, _, _, _) ->
            ctx |> linkExpr <| e
        | MatchExpr(expr, cases, _) ->
            let ctx' = linkExpr ctx expr
            cases |> List.fold (fun c (mc: MatchExprCase) -> linkExpr c mc.Body) ctx'
        | _ -> ctx

    /// Check function calls in a statement
    let rec private linkStmt (ctx: LinkContext) (stmt: Stmt) : LinkContext =
        match stmt with
        | VarDecl(_, _, init, _) ->
            match init with
            | Some expr -> linkExpr ctx expr
            | None -> ctx
        | Assignment(target, _, value, _) ->
            ctx |> linkExpr <| target |> linkExpr <| value
        | If(cond, thenBlock, elseIfs, elseBlock, _) ->
            let ctx' = linkExpr ctx cond
            let ctx'' = thenBlock |> List.fold linkStmt ctx'
            let ctx''' = elseIfs |> List.fold (fun c (e, stmts) ->
                let c' = linkExpr c e
                stmts |> List.fold linkStmt c') ctx''
            match elseBlock with
            | Some stmts -> stmts |> List.fold linkStmt ctx'''
            | None -> ctx'''
        | While(cond, body, _) ->
            let ctx' = linkExpr ctx cond
            body |> List.fold linkStmt ctx'
        | ForEach(_, collection, body, _) ->
            let ctx' = linkExpr ctx collection
            body |> List.fold linkStmt ctx'
        | For(_, startExpr, endExpr, stepExpr, _, body, _) ->
            let ctx' = linkExpr ctx startExpr
            let ctx' = linkExpr ctx' endExpr
            let ctx' = match stepExpr with Some e -> linkExpr ctx' e | None -> ctx'
            body |> List.fold linkStmt ctx'
        | Switch(expr, cases, defaultCase, _) ->
            let ctx' = linkExpr ctx expr
            let ctx'' = cases |> List.fold (fun c case ->
                let c' = case.Values |> List.fold linkExpr c
                case.Body |> List.fold linkStmt c') ctx'
            match defaultCase with
            | Some stmts -> stmts |> List.fold linkStmt ctx''
            | None -> ctx''
        | Return(valueOpt, _) ->
            match valueOpt with
            | Some expr -> linkExpr ctx expr
            | None -> ctx
        | Exit(valueOpt, _) ->
            match valueOpt with
            | Some expr -> linkExpr ctx expr
            | None -> ctx
        | Fail(msgOpt, _) ->
            match msgOpt with
            | Some expr -> linkExpr ctx expr
            | None -> ctx
        | FuncDef(_, _, body, _) ->
            // Traverse function body to resolve function calls within it
            body |> List.fold linkStmt ctx
        | UnionDef _ -> ctx
        | Match(expr, cases, _) ->
            let ctx' = linkExpr ctx expr
            cases |> List.fold (fun c mc -> mc.Body |> List.fold linkStmt c) ctx'
        | Break _ | Continue _ -> ctx
        | ExprStmt(expr, _) ->
            linkExpr ctx expr

    /// Linked program ready for execution
    type LinkedProgram =
        { Program: Program
          Functions: Map<string, IJyroFunction>
          UserFunctions: Map<string, JyroUserFunction>
          VariantConstructors: Map<string, JyroVariantConstructor> }

    /// Link a program with the available functions
    let link (program: Program) (functions: IJyroFunction seq) : JyroResult<LinkedProgram> =
        // Build initial context with host/stdlib functions
        let hostFunctionNames = functions |> Seq.map (fun f -> f.Name) |> Set.ofSeq
        let ctx =
            functions
            |> Seq.fold (fun (c: LinkContext) (f: IJyroFunction) -> c.AddFunction(f))
                { LinkContext.Empty with HostFunctions = hostFunctionNames }

        // First pass: extract FuncDef and UnionDef statements, create shells/constructors, check for shadowing
        let userFunctions = System.Collections.Generic.Dictionary<string, JyroUserFunction>()
        let variantConstructors = System.Collections.Generic.Dictionary<string, JyroVariantConstructor>()
        let ctx' =
            program.Statements |> List.fold (fun (c: LinkContext) stmt ->
                match stmt with
                | FuncDef(name, parameters, _, pos) ->
                    let loc = SourceLocation.Create(pos.Line, pos.Column)
                    // Check for shadowing of host/stdlib functions
                    if c.HostFunctions.Contains(name) then
                        c.AddError(MessageCode.FunctionOverride,
                            sprintf "Function '%s' overrides a built-in function" name,
                            [| box name |], loc)
                    // Check for duplicate user functions
                    elif userFunctions.ContainsKey(name) then
                        c.AddError(MessageCode.DuplicateFunction,
                            sprintf "Function '%s' is already defined" name,
                            [| box name |], loc)
                    // Check for conflict with variant constructors
                    elif variantConstructors.ContainsKey(name) then
                        c.AddError(MessageCode.FunctionShadowsVariant,
                            sprintf "Function '%s' shadows variant constructor '%s'" name name,
                            [| box name |], loc)
                    else
                        let shell = JyroUserFunction.createShell name parameters
                        userFunctions.[name] <- shell
                        c.AddFunction(shell)
                | UnionDef(unionName, variants, pos) ->
                    let loc = SourceLocation.Create(pos.Line, pos.Column)
                    variants |> List.fold (fun (c': LinkContext) variant ->
                        if c'.HostFunctions.Contains(variant.Name) then
                            c'.AddError(MessageCode.VariantShadowsFunction,
                                sprintf "Variant constructor '%s' shadows built-in function '%s'" variant.Name variant.Name,
                                [| box variant.Name |], loc)
                        elif userFunctions.ContainsKey(variant.Name) then
                            c'.AddError(MessageCode.FunctionShadowsVariant,
                                sprintf "Function '%s' shadows variant constructor '%s'" variant.Name variant.Name,
                                [| box variant.Name |], loc)
                        elif variantConstructors.ContainsKey(variant.Name) then
                            c'.AddError(MessageCode.DuplicateFunction,
                                sprintf "Variant constructor '%s' is already defined" variant.Name,
                                [| box variant.Name |], loc)
                        else
                            let ctor = JyroVariantConstructor.create unionName variant
                            variantConstructors.[variant.Name] <- ctor
                            c'.AddFunction(ctor)
                    ) c
                | _ -> c
            ) ctx

        // Second pass: traverse all statements (including function bodies) to resolve calls
        let ctx'' = program.Statements |> List.fold linkStmt ctx'
        let messages = ctx''.Messages |> List.rev
        if messages |> List.exists (fun m -> m.Severity = MessageSeverity.Error) then
            JyroResult<LinkedProgram>.Failure(messages)
        else
            let referencedFunctionMap =
                ctx''.ReferencedFunctions
                |> Set.toSeq
                |> Seq.choose (fun name -> ctx''.Functions.TryFind(name) |> Option.map (fun f -> name, f))
                |> Map.ofSeq
            let userFuncMap =
                userFunctions
                |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                |> Map.ofSeq
            let variantCtorMap =
                variantConstructors
                |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                |> Map.ofSeq
            let linkedProgram =
                { Program = program
                  Functions = referencedFunctionMap
                  UserFunctions = userFuncMap
                  VariantConstructors = variantCtorMap }
            { Value = Some linkedProgram; Messages = messages; IsSuccess = true }
