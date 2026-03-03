namespace Mesch.Jyro

open FParsec.Primitives
open FParsec.CharParsers
open Mesch.Jyro.Lexer
open Mesch.Jyro.PositionTracking

/// Expression parser for the Jyro language using manual precedence climbing
module ExpressionParser =
    // Forward reference for recursive expression parsing
    let pExpr, pExprRef = createParserForwardedToRef<Expr, unit>()

    /// Get position as Position type
    let private getPos' : Parser<Position, unit> = getPos

    // Primary expressions (literals, identifiers, grouped expressions)
    let private pNullLiteral : Parser<Expr, unit> =
        withPos nullLiteral |>> fun (_, pos) -> Literal(JyroNull.Instance, pos)

    let private pBoolLiteral : Parser<Expr, unit> =
        withPos booleanLiteral |>> fun (b, pos) -> Literal(JyroBoolean.FromBoolean(b), pos)

    let private pNumberLiteral : Parser<Expr, unit> =
        withPos numberLiteral |>> fun (n, pos) -> Literal(JyroNumber(n), pos)

    let private pStringLiteral : Parser<Expr, unit> =
        withPos anyStringLiteral |>> fun (s, pos) -> Literal(JyroString(s), pos)

    let private pIdentifier : Parser<Expr, unit> =
        withPos identifier |>> fun (name, pos) -> Identifier(name, pos)

    let private pGroupedExpr : Parser<Expr, unit> =
        between lparen rparen pExpr

    // Array literal [expr, expr, ...]
    let private pArrayLiteral : Parser<Expr, unit> =
        withPos (between lbracket rbracket (sepBy pExpr comma))
        |>> fun (elements, pos) -> ArrayLiteral(elements, pos)

    // Object literal { key: value, ... }
    let private pObjectProperty : Parser<string * Expr, unit> =
        (anyIdentifier <|> anyStringLiteral) .>> colon .>>. pExpr

    let private pObjectLiteral : Parser<Expr, unit> =
        withPos (between lbrace rbrace (sepBy pObjectProperty comma))
        |>> fun (props, pos) -> ObjectLiteral(props, pos)

    // Lambda expression: (params) => expr or param => expr
    let private pLambdaParams : Parser<string list, unit> =
        (between lparen rparen (sepBy identifier comma)) <|>
        (identifier |>> fun p -> [p])

    let private pLambda : Parser<Expr, unit> =
        withPos (pLambdaParams .>> arrow .>>. pExpr)
        |>> fun ((params', body), pos) -> Lambda(params', body, pos)

    // Match expression: match <expr> do case <Variant>[(bindings)] then <expr> ... end
    let private pMatchExprCase : Parser<MatchExprCase, unit> =
        withPos (
            keyword "case" >>. anyIdentifier .>>.
            (opt (attempt (between lparen rparen (sepBy identifier comma))) |>> Option.defaultValue []) .>>
            keyword "then" .>>.
            pExpr
        )
        |>> fun (((variantName, bindings), body), pos) ->
            { VariantName = variantName; Bindings = bindings; Body = body; Pos = pos }

    let private pMatchExpr : Parser<Expr, unit> =
        withPos (
            keyword "match" >>. pExpr .>> keyword "do" .>>.
            many1 pMatchExprCase .>>
            keyword "end"
        )
        |>> fun ((expr, cases), pos) -> MatchExpr(expr, cases, pos)

    // Primary expression (atom)
    let private pPrimary : Parser<Expr, unit> =
        choice [
            attempt pLambda
            pNullLiteral
            pBoolLiteral
            attempt pNumberLiteral
            pStringLiteral
            pArrayLiteral
            pObjectLiteral
            pMatchExpr
            pGroupedExpr
            pIdentifier
        ]

    // Postfix expressions (calls, property access, indexing, increment/decrement)
    type private PostfixOp =
        | CallOp of Expr list * Position
        | DotOp of string * Position
        | IndexOp of Expr * Position
        | PostIncOp of Position
        | PostDecOp of Position

    let private pCallArgs : Parser<Expr list, unit> = between lparen rparen (sepBy pExpr comma)

    let private pPostfixOp : Parser<PostfixOp, unit> =
        choice [
            withPos pCallArgs |>> fun (args, pos) -> CallOp(args, pos)
            dot >>. withPos anyIdentifier |>> fun (name, pos) -> DotOp(name, pos)
            withPos (between lbracket rbracket pExpr) |>> fun (idx, pos) -> IndexOp(idx, pos)
            withPos incrementOp |>> fun (_, pos) -> PostIncOp(pos)
            withPos decrementOp |>> fun (_, pos) -> PostDecOp(pos)
        ]

    let private applyPostfix (baseExpr: Expr) (op: PostfixOp) : Expr =
        match op with
        | CallOp(args, pos) ->
            match baseExpr with
            | Identifier(name, _) -> Call(name, args, pos)
            | _ -> failwith "Function calls must be on identifiers"
        | DotOp(prop, pos) -> PropertyAccess(baseExpr, prop, pos)
        | IndexOp(idx, pos) -> IndexAccess(baseExpr, idx, pos)
        | PostIncOp(pos) -> IncrementDecrement(baseExpr, true, false, pos)
        | PostDecOp(pos) -> IncrementDecrement(baseExpr, false, false, pos)

    let private pPostfixExpr : Parser<Expr, unit> =
        pPrimary .>>. many pPostfixOp
        |>> fun (base', ops) -> List.fold applyPostfix base' ops

    // Prefix/unary expressions (only numeric negate at this level)
    let private pNegateOp : Parser<UnaryOp * Position, unit> =
        withPos (symbol "-") |>> fun (_, pos) -> (Negate, pos)

    let private pUnaryExpr : Parser<Expr, unit> =
        let rec impl stream =
            (choice [
                attempt (withPos incrementOp .>>. pPostfixExpr |>> fun ((_, pos), e) -> IncrementDecrement(e, true, true, pos))
                attempt (withPos decrementOp .>>. pPostfixExpr |>> fun ((_, pos), e) -> IncrementDecrement(e, false, true, pos))
                attempt (pNegateOp .>>. impl |>> fun ((op, pos), e) -> Unary(op, e, pos))
                pPostfixExpr
            ]) stream
        impl

    // Binary operators with precedence (using manual precedence climbing)
    // Helper: match binary operator symbol NOT followed by '=' (to avoid consuming *= /= %= += -=)
    let private binOpSym s : Parser<string, unit> = attempt (lexeme (pstring s .>> notFollowedBy (pchar '=')))

    // Multiplicative: * / %
    let private pMulOp : Parser<BinaryOp, unit> =
        (binOpSym "*" >>% Multiply) <|> (binOpSym "/" >>% Divide) <|> (binOpSym "%" >>% Modulo)

    let private pMultiplicativeExpr : Parser<Expr, unit> =
        chainl1 pUnaryExpr (pMulOp |>> fun op -> fun l r -> Binary(l, op, r, l.Position))

    // Additive: + -
    let private pAddOp : Parser<BinaryOp, unit> =
        (binOpSym "+" >>% Add) <|> (binOpSym "-" >>% Subtract)

    let private pAdditiveExpr : Parser<Expr, unit> =
        chainl1 pMultiplicativeExpr (pAddOp |>> fun op -> fun l r -> Binary(l, op, r, l.Position))

    // Relational: < <= > >=
    let private pRelOp : Parser<BinaryOp, unit> =
        choice [
            attempt (symbol "<=" >>% LessThanOrEqual)
            attempt (symbol ">=" >>% GreaterThanOrEqual)
            (symbol "<" >>% LessThan)
            (symbol ">" >>% GreaterThan)
        ]

    let private pRelationalExpr : Parser<Expr, unit> =
        chainl1 pAdditiveExpr (pRelOp |>> fun op -> fun l r -> Binary(l, op, r, l.Position))

    // Equality: == !=
    let private pEqOp : Parser<BinaryOp, unit> =
        (symbol "==" >>% Equal) <|> (symbol "!=" >>% NotEqual)

    let private pEqualityExpr : Parser<Expr, unit> =
        chainl1 pRelationalExpr (pEqOp |>> fun op -> fun l r -> Binary(l, op, r, l.Position))

    // Type check expression: expr is type or expr is not type
    // Placed between equality and logical AND so that "x is number and y > 0" works
    let private pTypeCheck : Parser<Expr, unit> =
        pEqualityExpr .>>. opt (keyword "is" >>. opt (keyword "not") .>>. typeKeyword)
        |>> fun (e, typeOpt) ->
            match typeOpt with
            | Some (neg, t) -> TypeCheck(e, t, neg.IsSome, e.Position)
            | None -> e

    // Logical NOT: not (binds between type check and AND, like Python)
    let private pNotExpr : Parser<Expr, unit> =
        let rec impl stream =
            (choice [
                attempt (withPos notOp .>>. impl |>> fun ((_, pos), e) -> Unary(Not, e, pos))
                pTypeCheck
            ]) stream
        impl

    // Logical AND: and
    let private pAndOp : Parser<BinaryOp, unit> =
        keyword "and" >>% And

    let private pAndExpr : Parser<Expr, unit> =
        chainl1 pNotExpr (pAndOp |>> fun op -> fun l r -> Binary(l, op, r, l.Position))

    // Logical OR: or
    let private pOrOp : Parser<BinaryOp, unit> =
        keyword "or" >>% Or

    let private pOrExpr : Parser<Expr, unit> =
        chainl1 pAndExpr (pOrOp |>> fun op -> fun l r -> Binary(l, op, r, l.Position))

    // Null coalescing: ??
    let private pCoalesceExpr : Parser<Expr, unit> =
        chainr1 pOrExpr (symbol "??" >>% fun l r -> Binary(l, Coalesce, r, l.Position))

    // Ternary conditional expression
    let private pTernary : Parser<Expr, unit> =
        pCoalesceExpr .>>. opt (questionMark >>. pExpr .>> colon .>>. pExpr)
        |>> fun (cond, ternaryOpt) ->
            match ternaryOpt with
            | Some (thenExpr, elseExpr) -> Ternary(cond, thenExpr, elseExpr, cond.Position)
            | None -> cond

    // Finalize the expression parser
    do pExprRef.Value <- pTernary

    /// Parse an expression
    let parseExpr : Parser<Expr, unit> = pExpr

    /// Parse a comma-separated list of expressions
    let parseExprList : Parser<Expr list, unit> = sepBy pExpr comma
