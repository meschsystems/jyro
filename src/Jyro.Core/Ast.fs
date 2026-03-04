namespace Mesch.Jyro

/// Position information in source code
[<Struct>]
type Position =
    { Line: int
      Column: int
      StartIndex: int
      EndIndex: int }

    static member None = { Line = 0; Column = 0; StartIndex = 0; EndIndex = 0 }

    static member Create(line: int, column: int, ?startIndex: int, ?endIndex: int) =
        { Line = line
          Column = column
          StartIndex = defaultArg startIndex 0
          EndIndex = defaultArg endIndex 0 }

/// Type hints in variable declarations
type JyroType =
    | AnyType
    | NumberType
    | StringType
    | BooleanType
    | ObjectType
    | ArrayType
    | NullType

/// A variant in a union declaration
type UnionVariant =
    { Name: string
      Fields: (string * JyroType option) list }

/// Expression AST nodes
type Expr =
    /// A literal value (number, string, boolean, null)
    | Literal of value: JyroValue * pos: Position
    /// A variable or identifier reference
    | Identifier of name: string * pos: Position
    /// A binary operation (e.g., a + b)
    | Binary of left: Expr * op: BinaryOp * right: Expr * pos: Position
    /// A unary operation (e.g., -x, !y)
    | Unary of op: UnaryOp * operand: Expr * pos: Position
    /// A ternary/conditional expression (e.g., a ? b : c)
    | Ternary of condition: Expr * thenExpr: Expr * elseExpr: Expr * pos: Position
    /// A function call (e.g., Func(a, b))
    | Call of name: string * args: CallArgs * pos: Position
    /// Property access using dot notation (e.g., obj.prop)
    | PropertyAccess of target: Expr * property: string * pos: Position
    /// Index access using bracket notation (e.g., arr[0])
    | IndexAccess of target: Expr * index: Expr * pos: Position
    /// Object literal (e.g., { a: 1, b: 2 })
    | ObjectLiteral of properties: (string * Expr) list * pos: Position
    /// Array literal (e.g., [1, 2, 3])
    | ArrayLiteral of elements: Expr list * pos: Position
    /// Lambda expression (e.g., x => x + 1)
    | Lambda of parameters: string list * body: Expr * pos: Position
    /// Type check expression (e.g., x is string)
    | TypeCheck of expr: Expr * typeName: JyroType * isNegated: bool * pos: Position
    /// Pre/post increment/decrement (e.g., ++x, x--)
    | IncrementDecrement of expr: Expr * isIncrement: bool * isPrefix: bool * pos: Position
    /// Match expression (each arm is a single expression that produces a value)
    | MatchExpr of expr: Expr * cases: MatchExprCase list * pos: Position

    /// Get the position of this expression
    member this.Position =
        match this with
        | Literal(_, pos) -> pos
        | Identifier(_, pos) -> pos
        | Binary(_, _, _, pos) -> pos
        | Unary(_, _, pos) -> pos
        | Ternary(_, _, _, pos) -> pos
        | Call(_, _, pos) -> pos
        | PropertyAccess(_, _, pos) -> pos
        | IndexAccess(_, _, pos) -> pos
        | ObjectLiteral(_, pos) -> pos
        | ArrayLiteral(_, pos) -> pos
        | Lambda(_, _, pos) -> pos
        | TypeCheck(_, _, _, pos) -> pos
        | IncrementDecrement(_, _, _, pos) -> pos
        | MatchExpr(_, _, pos) -> pos

/// A case in a match expression (body is a single expression)
and MatchExprCase =
    { VariantName: string
      Bindings: string list
      Body: Expr
      Pos: Position }

/// Arguments to a function call — either all positional or all named
and CallArgs =
    | PositionalArgs of Expr list
    | NamedArgs of (string * Expr) list

/// A parameter in a function definition, with optional type hint and optional default value
and FuncParam =
    { Name: string
      TypeHint: JyroType option
      DefaultValue: Expr option }

/// Direction for range-based for loops
type ForDirection =
    | Ascending
    | Descending

/// Statement AST nodes
type Stmt =
    /// Variable declaration (e.g., var x = 5)
    | VarDecl of name: string * typeHint: JyroType option * init: Expr option * pos: Position
    /// Assignment (e.g., x = 5, x += 3)
    | Assignment of target: Expr * op: AssignOp * value: Expr * pos: Position
    /// If statement with optional elseif chains and else block
    | If of condition: Expr * thenBlock: Stmt list * elseIfs: (Expr * Stmt list) list * elseBlock: Stmt list option * pos: Position
    /// While loop
    | While of condition: Expr * body: Stmt list * pos: Position
    /// Foreach loop
    | ForEach of varName: string * collection: Expr * body: Stmt list * pos: Position
    /// Range-based for loop (e.g., for i in 0 to 10 by 2 do ... end)
    | For of varName: string * startExpr: Expr * endExpr: Expr * stepExpr: Expr option * direction: ForDirection * body: Stmt list * pos: Position
    /// Switch statement
    | Switch of expr: Expr * cases: SwitchCase list * defaultCase: Stmt list option * pos: Position
    /// Return statement (returns a value from a function)
    | Return of value: Expr option * pos: Position
    /// Fail statement (business logic failure)
    | Fail of message: Expr option * pos: Position
    /// Exit statement (clean script termination)
    | Exit of value: Expr option * pos: Position
    /// Function definition
    | FuncDef of name: string * parameters: FuncParam list * body: Stmt list * pos: Position
    /// Break statement (exits loop)
    | Break of pos: Position
    /// Continue statement (next iteration)
    | Continue of pos: Position
    /// Expression statement (expression evaluated for side effects)
    | ExprStmt of expr: Expr * pos: Position
    /// Union type declaration (e.g., union Shape Circle(radius: number) ... end)
    | UnionDef of name: string * variants: UnionVariant list * pos: Position
    /// Match statement for union pattern matching
    | Match of expr: Expr * cases: MatchCase list * pos: Position

    /// Get the position of this statement
    member this.Position =
        match this with
        | VarDecl(_, _, _, pos) -> pos
        | Assignment(_, _, _, pos) -> pos
        | If(_, _, _, _, pos) -> pos
        | While(_, _, pos) -> pos
        | ForEach(_, _, _, pos) -> pos
        | For(_, _, _, _, _, _, pos) -> pos
        | Switch(_, _, _, pos) -> pos
        | Return(_, pos) -> pos
        | Fail(_, pos) -> pos
        | Exit(_, pos) -> pos
        | FuncDef(_, _, _, pos) -> pos
        | Break pos -> pos
        | Continue pos -> pos
        | ExprStmt(_, pos) -> pos
        | UnionDef(_, _, pos) -> pos
        | Match(_, _, pos) -> pos

/// A case in a switch statement
and SwitchCase =
    { Values: Expr list
      Body: Stmt list }

/// A case in a match statement
and MatchCase =
    { VariantName: string
      Bindings: string list
      Body: Stmt list
      Pos: Position }

/// A complete Jyro program
type Program =
    { Statements: Stmt list }

/// Helper module for AST operations
module Ast =
    /// Check if an expression is a valid assignment target
    let rec isAssignmentTarget = function
        | Identifier _ -> true
        | PropertyAccess _ -> true
        | IndexAccess _ -> true
        | _ -> false

    /// Check if a statement is a control flow terminator
    let isTerminator = function
        | Return _ | Fail _ | Exit _ | Break _ | Continue _ -> true
        | _ -> false

    /// Get all identifiers referenced in an expression
    let rec getIdentifiers expr =
        match expr with
        | Identifier(name, _) -> [ name ]
        | Binary(l, _, r, _) -> getIdentifiers l @ getIdentifiers r
        | Unary(_, e, _) -> getIdentifiers e
        | Ternary(c, t, e, _) -> getIdentifiers c @ getIdentifiers t @ getIdentifiers e
        | Call(_, args, _) ->
            match args with
            | PositionalArgs exprs -> exprs |> List.collect getIdentifiers
            | NamedArgs pairs -> pairs |> List.collect (fun (_, expr) -> getIdentifiers expr)
        | PropertyAccess(t, _, _) -> getIdentifiers t
        | IndexAccess(t, i, _) -> getIdentifiers t @ getIdentifiers i
        | ObjectLiteral(props, _) -> props |> List.collect (snd >> getIdentifiers)
        | ArrayLiteral(elems, _) -> elems |> List.collect getIdentifiers
        | Lambda(params', body, _) ->
            let bodyIds = getIdentifiers body
            bodyIds |> List.filter (fun id -> not (List.contains id params'))
        | TypeCheck(e, _, _, _) -> getIdentifiers e
        | IncrementDecrement(e, _, _, _) -> getIdentifiers e
        | MatchExpr(e, cases, _) ->
            let exprIds = getIdentifiers e
            let caseIds = cases |> List.collect (fun c ->
                let bodyIds = getIdentifiers c.Body
                bodyIds |> List.filter (fun id -> not (List.contains id c.Bindings)))
            exprIds @ caseIds
        | Literal _ -> []
