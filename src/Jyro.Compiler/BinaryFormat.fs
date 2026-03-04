namespace Mesch.Jyro

open System
open System.IO
open System.Text

/// Header metadata from a .jyrx binary file (no AST deserialization)
type JyrxHeader =
    { Version: int
      SourceHash: byte[]
      RequiredFunctions: string list }

/// Result of deserializing a .jyrx binary file
type DeserializedProgram =
    { Program: Program
      RequiredFunctions: string list
      SourceHash: byte[]
      Version: int }

/// Binary serialization format for compiled Jyro programs (.jyrx files).
/// Encodes the validated AST and function dependency table into a compact binary representation.
module BinaryFormat =
    let [<Literal>] private MagicBytes = "JYRX"
    let [<Literal>] private FormatVersion = 6us
    let [<Literal>] private HeaderSize = 44

    // --- Writer helpers ---

    let private writeString (w: BinaryWriter) (s: string) =
        let bytes = Encoding.UTF8.GetBytes(s)
        w.Write(uint16 bytes.Length)
        w.Write(bytes)

    let private writePosition (w: BinaryWriter) (pos: Position) =
        w.Write(pos.Line)
        w.Write(pos.Column)
        w.Write(pos.StartIndex)
        w.Write(pos.EndIndex)

    let private writeOption (w: BinaryWriter) (writeValue: BinaryWriter -> 'a -> unit) (opt: 'a option) =
        match opt with
        | None -> w.Write(0uy)
        | Some v ->
            w.Write(1uy)
            writeValue w v

    let private writeList (w: BinaryWriter) (writeItem: BinaryWriter -> 'a -> unit) (items: 'a list) =
        w.Write(uint16 items.Length)
        items |> List.iter (writeItem w)

    let private writeJyroType (w: BinaryWriter) (t: JyroType) =
        let tag =
            match t with
            | AnyType -> 0uy
            | NumberType -> 1uy
            | StringType -> 2uy
            | BooleanType -> 3uy
            | ObjectType -> 4uy
            | ArrayType -> 5uy
            | NullType -> 6uy
        w.Write(tag)

    let private writeBinaryOp (w: BinaryWriter) (op: BinaryOp) =
        let tag =
            match op with
            | Add -> 0uy
            | Subtract -> 1uy
            | Multiply -> 2uy
            | Divide -> 3uy
            | Modulo -> 4uy
            | Equal -> 5uy
            | NotEqual -> 6uy
            | LessThan -> 7uy
            | LessThanOrEqual -> 8uy
            | GreaterThan -> 9uy
            | GreaterThanOrEqual -> 10uy
            | And -> 11uy
            | Or -> 12uy
            | Coalesce -> 13uy
        w.Write(tag)

    let private writeUnaryOp (w: BinaryWriter) (op: UnaryOp) =
        let tag =
            match op with
            | Negate -> 0uy
            | Not -> 1uy
        w.Write(tag)

    let private writeAssignOp (w: BinaryWriter) (op: AssignOp) =
        let tag =
            match op with
            | Assign -> 0uy
            | AddAssign -> 1uy
            | SubtractAssign -> 2uy
            | MultiplyAssign -> 3uy
            | DivideAssign -> 4uy
            | ModuloAssign -> 5uy
        w.Write(tag)

    let private writeLiteralValue (w: BinaryWriter) (value: JyroValue) =
        match value with
        | :? JyroNull ->
            w.Write(1uy)
        | :? JyroBoolean as b ->
            w.Write(2uy)
            w.Write(b.Value)
        | :? JyroNumber as n ->
            w.Write(3uy)
            w.Write(n.Value)
        | :? JyroString as s ->
            w.Write(4uy)
            writeString w s.Value
        | _ ->
            w.Write(1uy) // fallback to null for unexpected types

    let rec private writeExpr (w: BinaryWriter) (expr: Expr) =
        match expr with
        | Literal(value, pos) ->
            w.Write(0x01uy)
            writeLiteralValue w value
            writePosition w pos
        | Identifier(name, pos) ->
            w.Write(0x02uy)
            writeString w name
            writePosition w pos
        | Binary(left, op, right, pos) ->
            w.Write(0x03uy)
            writeExpr w left
            writeBinaryOp w op
            writeExpr w right
            writePosition w pos
        | Unary(op, operand, pos) ->
            w.Write(0x04uy)
            writeUnaryOp w op
            writeExpr w operand
            writePosition w pos
        | Ternary(cond, thenExpr, elseExpr, pos) ->
            w.Write(0x05uy)
            writeExpr w cond
            writeExpr w thenExpr
            writeExpr w elseExpr
            writePosition w pos
        | Call(name, args, pos) ->
            w.Write(0x06uy)
            writeString w name
            match args with
            | PositionalArgs exprs ->
                w.Write(0uy)
                writeList w writeExpr exprs
            | NamedArgs pairs ->
                w.Write(1uy)
                w.Write(uint16 pairs.Length)
                for (argName, expr) in pairs do
                    writeString w argName
                    writeExpr w expr
            writePosition w pos
        | PropertyAccess(target, prop, pos) ->
            w.Write(0x07uy)
            writeExpr w target
            writeString w prop
            writePosition w pos
        | IndexAccess(target, index, pos) ->
            w.Write(0x08uy)
            writeExpr w target
            writeExpr w index
            writePosition w pos
        | ObjectLiteral(props, pos) ->
            w.Write(0x09uy)
            w.Write(uint16 props.Length)
            props |> List.iter (fun (key, value) ->
                writeString w key
                writeExpr w value)
            writePosition w pos
        | ArrayLiteral(elements, pos) ->
            w.Write(0x0Auy)
            writeList w writeExpr elements
            writePosition w pos
        | Lambda(parameters, body, pos) ->
            w.Write(0x0Buy)
            writeList w writeString parameters
            writeExpr w body
            writePosition w pos
        | TypeCheck(expr, typeName, isNegated, pos) ->
            w.Write(0x0Cuy)
            writeExpr w expr
            writeJyroType w typeName
            w.Write(if isNegated then 1uy else 0uy)
            writePosition w pos
        | IncrementDecrement(expr, isIncrement, isPrefix, pos) ->
            w.Write(0x0Duy)
            writeExpr w expr
            w.Write(if isIncrement then 1uy else 0uy)
            w.Write(if isPrefix then 1uy else 0uy)
            writePosition w pos
        | MatchExpr(expr, cases, pos) ->
            w.Write(0x0Euy)
            writeExpr w expr
            writeList w writeMatchExprCase cases
            writePosition w pos

    and private writeMatchExprCase (w: BinaryWriter) (case: MatchExprCase) =
        writeString w case.VariantName
        writeList w writeString case.Bindings
        writeExpr w case.Body
        writePosition w case.Pos

    and private writeSwitchCase (w: BinaryWriter) (case: SwitchCase) =
        writeList w writeExpr case.Values
        writeList w writeStmt case.Body

    and private writeUnionVariant (w: BinaryWriter) (variant: UnionVariant) =
        writeString w variant.Name
        w.Write(uint16 variant.Fields.Length)
        variant.Fields |> List.iter (fun (name, typeHint) ->
            writeString w name
            writeOption w writeJyroType typeHint)

    and private writeMatchCase (w: BinaryWriter) (case: MatchCase) =
        writeString w case.VariantName
        writeList w writeString case.Bindings
        writeList w writeStmt case.Body
        writePosition w case.Pos

    and private writeStmt (w: BinaryWriter) (stmt: Stmt) =
        match stmt with
        | VarDecl(name, typeHint, init, pos) ->
            w.Write(0x20uy)
            writeString w name
            writeOption w writeJyroType typeHint
            writeOption w writeExpr init
            writePosition w pos
        | Assignment(target, op, value, pos) ->
            w.Write(0x21uy)
            writeExpr w target
            writeAssignOp w op
            writeExpr w value
            writePosition w pos
        | If(cond, thenBlock, elseIfs, elseBlock, pos) ->
            w.Write(0x22uy)
            writeExpr w cond
            writeList w writeStmt thenBlock
            w.Write(uint16 elseIfs.Length)
            elseIfs |> List.iter (fun (e, stmts) ->
                writeExpr w e
                writeList w writeStmt stmts)
            writeOption w (fun wr stmts -> writeList wr writeStmt stmts) elseBlock
            writePosition w pos
        | While(cond, body, pos) ->
            w.Write(0x23uy)
            writeExpr w cond
            writeList w writeStmt body
            writePosition w pos
        | ForEach(varName, collection, body, pos) ->
            w.Write(0x24uy)
            writeString w varName
            writeExpr w collection
            writeList w writeStmt body
            writePosition w pos
        | For(varName, startExpr, endExpr, stepExpr, direction, body, pos) ->
            w.Write(0x25uy)
            writeString w varName
            writeExpr w startExpr
            writeExpr w endExpr
            writeOption w writeExpr stepExpr
            w.Write(match direction with Ascending -> 0uy | Descending -> 1uy)
            writeList w writeStmt body
            writePosition w pos
        | Switch(expr, cases, defaultCase, pos) ->
            w.Write(0x26uy)
            writeExpr w expr
            writeList w writeSwitchCase cases
            writeOption w (fun wr stmts -> writeList wr writeStmt stmts) defaultCase
            writePosition w pos
        | Return(value, pos) ->
            w.Write(0x27uy)
            writeOption w writeExpr value
            writePosition w pos
        | Fail(message, pos) ->
            w.Write(0x28uy)
            writeOption w writeExpr message
            writePosition w pos
        | Break pos ->
            w.Write(0x29uy)
            writePosition w pos
        | Continue pos ->
            w.Write(0x2Auy)
            writePosition w pos
        | ExprStmt(expr, pos) ->
            w.Write(0x2Buy)
            writeExpr w expr
            writePosition w pos
        | FuncDef(name, parameters, body, pos) ->
            w.Write(0x2Cuy)
            writeString w name
            w.Write(uint16 parameters.Length)
            parameters |> List.iter (fun (p: FuncParam) ->
                writeString w p.Name
                writeOption w writeJyroType p.TypeHint
                writeOption w writeExpr p.DefaultValue)
            writeList w writeStmt body
            writePosition w pos
        | Exit(value, pos) ->
            w.Write(0x2Duy)
            writeOption w writeExpr value
            writePosition w pos
        | UnionDef(name, variants, pos) ->
            w.Write(0x2Euy)
            writeString w name
            writeList w writeUnionVariant variants
            writePosition w pos
        | Match(expr, cases, pos) ->
            w.Write(0x2Fuy)
            writeExpr w expr
            writeList w writeMatchCase cases
            writePosition w pos

    // --- Reader helpers ---

    // Safety limits for deserialization to prevent malicious .jyrx files from
    // causing excessive memory allocation or stack overflow.
    let [<Literal>] private MaxFileSize = 10 * 1024 * 1024  // 10 MB
    let [<Literal>] private MaxStringLength = 65535
    let [<Literal>] private MaxListCount = 10000
    let [<Literal>] private MaxFunctionCount = 1000
    let [<Literal>] private MaxRecursionDepth = 200

    let private readString (r: BinaryReader) =
        let len = int (r.ReadUInt16())
        if len > MaxStringLength then
            failwithf "String length %d exceeds maximum of %d" len MaxStringLength
        let bytes = r.ReadBytes(len)
        Encoding.UTF8.GetString(bytes)

    let private readPosition (r: BinaryReader) =
        { Line = r.ReadInt32()
          Column = r.ReadInt32()
          StartIndex = r.ReadInt32()
          EndIndex = r.ReadInt32() }

    let private readOption (r: BinaryReader) (readValue: BinaryReader -> 'a) : 'a option =
        match r.ReadByte() with
        | 0uy -> None
        | _ -> Some (readValue r)

    let private readList (r: BinaryReader) (readItem: BinaryReader -> 'a) : 'a list =
        let count = int (r.ReadUInt16())
        if count > MaxListCount then
            failwithf "List count %d exceeds maximum of %d" count MaxListCount
        [ for _ in 1 .. count -> readItem r ]

    let private readJyroType (r: BinaryReader) =
        match r.ReadByte() with
        | 0uy -> AnyType
        | 1uy -> NumberType
        | 2uy -> StringType
        | 3uy -> BooleanType
        | 4uy -> ObjectType
        | 5uy -> ArrayType
        | 6uy -> NullType
        | tag -> failwithf "Unknown JyroType tag: %d" tag

    let private readBinaryOp (r: BinaryReader) =
        match r.ReadByte() with
        | 0uy -> Add
        | 1uy -> Subtract
        | 2uy -> Multiply
        | 3uy -> Divide
        | 4uy -> Modulo
        | 5uy -> Equal
        | 6uy -> NotEqual
        | 7uy -> LessThan
        | 8uy -> LessThanOrEqual
        | 9uy -> GreaterThan
        | 10uy -> GreaterThanOrEqual
        | 11uy -> And
        | 12uy -> Or
        | 13uy -> Coalesce
        | tag -> failwithf "Unknown BinaryOp tag: %d" tag

    let private readUnaryOp (r: BinaryReader) =
        match r.ReadByte() with
        | 0uy -> Negate
        | 1uy -> Not
        | tag -> failwithf "Unknown UnaryOp tag: %d" tag

    let private readAssignOp (r: BinaryReader) =
        match r.ReadByte() with
        | 0uy -> Assign
        | 1uy -> AddAssign
        | 2uy -> SubtractAssign
        | 3uy -> MultiplyAssign
        | 4uy -> DivideAssign
        | 5uy -> ModuloAssign
        | tag -> failwithf "Unknown AssignOp tag: %d" tag

    let private readLiteralValue (r: BinaryReader) : JyroValue =
        match r.ReadByte() with
        | 1uy -> JyroNull.Instance :> JyroValue
        | 2uy -> JyroBoolean.FromBoolean(r.ReadBoolean()) :> JyroValue
        | 3uy -> JyroNumber(r.ReadDouble()) :> JyroValue
        | 4uy -> JyroString(readString r) :> JyroValue
        | tag -> failwithf "Unknown literal value tag: %d" tag

    let mutable private deserializeDepth = 0

    let rec private readExpr (r: BinaryReader) : Expr =
        deserializeDepth <- deserializeDepth + 1
        if deserializeDepth > MaxRecursionDepth then
            failwithf "AST nesting depth exceeds maximum of %d" MaxRecursionDepth
        try
            match r.ReadByte() with
            | 0x01uy ->
                let value = readLiteralValue r
                let pos = readPosition r
                Literal(value, pos)
            | 0x02uy ->
                let name = readString r
                let pos = readPosition r
                Identifier(name, pos)
            | 0x03uy ->
                let left = readExpr r
                let op = readBinaryOp r
                let right = readExpr r
                let pos = readPosition r
                Binary(left, op, right, pos)
            | 0x04uy ->
                let op = readUnaryOp r
                let operand = readExpr r
                let pos = readPosition r
                Unary(op, operand, pos)
            | 0x05uy ->
                let cond = readExpr r
                let thenExpr = readExpr r
                let elseExpr = readExpr r
                let pos = readPosition r
                Ternary(cond, thenExpr, elseExpr, pos)
            | 0x06uy ->
                let name = readString r
                let args =
                    match r.ReadByte() with
                    | 0uy -> PositionalArgs(readList r readExpr)
                    | _ ->
                        let count = int (r.ReadUInt16())
                        if count > MaxListCount then
                            failwithf "Named argument count %d exceeds maximum of %d" count MaxListCount
                        let pairs = [ for _ in 1 .. count -> (readString r, readExpr r) ]
                        NamedArgs(pairs)
                let pos = readPosition r
                Call(name, args, pos)
            | 0x07uy ->
                let target = readExpr r
                let prop = readString r
                let pos = readPosition r
                PropertyAccess(target, prop, pos)
            | 0x08uy ->
                let target = readExpr r
                let index = readExpr r
                let pos = readPosition r
                IndexAccess(target, index, pos)
            | 0x09uy ->
                let count = int (r.ReadUInt16())
                if count > MaxListCount then
                    failwithf "Object property count %d exceeds maximum of %d" count MaxListCount
                let props = [ for _ in 1 .. count -> (readString r, readExpr r) ]
                let pos = readPosition r
                ObjectLiteral(props, pos)
            | 0x0Auy ->
                let elements = readList r readExpr
                let pos = readPosition r
                ArrayLiteral(elements, pos)
            | 0x0Buy ->
                let parameters = readList r readString
                let body = readExpr r
                let pos = readPosition r
                Lambda(parameters, body, pos)
            | 0x0Cuy ->
                let expr = readExpr r
                let typeName = readJyroType r
                let isNegated = r.ReadByte() <> 0uy
                let pos = readPosition r
                TypeCheck(expr, typeName, isNegated, pos)
            | 0x0Duy ->
                let expr = readExpr r
                let isIncrement = r.ReadByte() <> 0uy
                let isPrefix = r.ReadByte() <> 0uy
                let pos = readPosition r
                IncrementDecrement(expr, isIncrement, isPrefix, pos)
            | 0x0Euy ->
                let expr = readExpr r
                let cases = readList r readMatchExprCase
                let pos = readPosition r
                MatchExpr(expr, cases, pos)
            | tag -> failwithf "Unknown Expr tag: 0x%02X" tag
        finally
            deserializeDepth <- deserializeDepth - 1

    and private readSwitchCase (r: BinaryReader) : SwitchCase =
        let values = readList r readExpr
        let body = readList r readStmt
        { Values = values; Body = body }

    and private readUnionVariant (r: BinaryReader) : UnionVariant =
        let name = readString r
        let fieldCount = int (r.ReadUInt16())
        if fieldCount > MaxListCount then
            failwithf "Field count %d exceeds maximum of %d" fieldCount MaxListCount
        let fields = [ for _ in 1 .. fieldCount -> (readString r, readOption r readJyroType) ]
        { Name = name; Fields = fields }

    and private readMatchCase (r: BinaryReader) : MatchCase =
        let variantName = readString r
        let bindings = readList r readString
        let body = readList r readStmt
        let pos = readPosition r
        { VariantName = variantName; Bindings = bindings; Body = body; Pos = pos }

    and private readMatchExprCase (r: BinaryReader) : MatchExprCase =
        let variantName = readString r
        let bindings = readList r readString
        let body = readExpr r
        let pos = readPosition r
        { VariantName = variantName; Bindings = bindings; Body = body; Pos = pos }

    and private readStmt (r: BinaryReader) : Stmt =
        deserializeDepth <- deserializeDepth + 1
        if deserializeDepth > MaxRecursionDepth then
            failwithf "AST nesting depth exceeds maximum of %d" MaxRecursionDepth
        try
            match r.ReadByte() with
            | 0x20uy ->
                let name = readString r
                let typeHint = readOption r readJyroType
                let init = readOption r readExpr
                let pos = readPosition r
                VarDecl(name, typeHint, init, pos)
            | 0x21uy ->
                let target = readExpr r
                let op = readAssignOp r
                let value = readExpr r
                let pos = readPosition r
                Assignment(target, op, value, pos)
            | 0x22uy ->
                let cond = readExpr r
                let thenBlock = readList r readStmt
                let elseIfCount = int (r.ReadUInt16())
                if elseIfCount > MaxListCount then
                    failwithf "ElseIf count %d exceeds maximum of %d" elseIfCount MaxListCount
                let elseIfs = [ for _ in 1 .. elseIfCount -> (readExpr r, readList r readStmt) ]
                let elseBlock = readOption r (fun rd -> readList rd readStmt)
                let pos = readPosition r
                If(cond, thenBlock, elseIfs, elseBlock, pos)
            | 0x23uy ->
                let cond = readExpr r
                let body = readList r readStmt
                let pos = readPosition r
                While(cond, body, pos)
            | 0x24uy ->
                let varName = readString r
                let collection = readExpr r
                let body = readList r readStmt
                let pos = readPosition r
                ForEach(varName, collection, body, pos)
            | 0x25uy ->
                let varName = readString r
                let startExpr = readExpr r
                let endExpr = readExpr r
                let stepExpr = readOption r readExpr
                let direction = match r.ReadByte() with 0uy -> Ascending | _ -> Descending
                let body = readList r readStmt
                let pos = readPosition r
                For(varName, startExpr, endExpr, stepExpr, direction, body, pos)
            | 0x26uy ->
                let expr = readExpr r
                let cases = readList r readSwitchCase
                let defaultCase = readOption r (fun rd -> readList rd readStmt)
                let pos = readPosition r
                Switch(expr, cases, defaultCase, pos)
            | 0x27uy ->
                let value = readOption r readExpr
                let pos = readPosition r
                Return(value, pos)
            | 0x28uy ->
                let message = readOption r readExpr
                let pos = readPosition r
                Fail(message, pos)
            | 0x29uy ->
                let pos = readPosition r
                Break pos
            | 0x2Auy ->
                let pos = readPosition r
                Continue pos
            | 0x2Buy ->
                let expr = readExpr r
                let pos = readPosition r
                ExprStmt(expr, pos)
            | 0x2Cuy ->
                let name = readString r
                let paramCount = int (r.ReadUInt16())
                if paramCount > MaxListCount then
                    failwithf "Parameter count %d exceeds maximum of %d" paramCount MaxListCount
                let parameters =
                    [ for _ in 1 .. paramCount ->
                        { Name = readString r
                          TypeHint = readOption r readJyroType
                          DefaultValue = readOption r readExpr } ]
                let body = readList r readStmt
                let pos = readPosition r
                FuncDef(name, parameters, body, pos)
            | 0x2Duy ->
                let value = readOption r readExpr
                let pos = readPosition r
                Exit(value, pos)
            | 0x2Euy ->
                let name = readString r
                let variants = readList r readUnionVariant
                let pos = readPosition r
                UnionDef(name, variants, pos)
            | 0x2Fuy ->
                let expr = readExpr r
                let cases = readList r readMatchCase
                let pos = readPosition r
                Match(expr, cases, pos)
            | tag -> failwithf "Unknown Stmt tag: 0x%02X" tag
        finally
            deserializeDepth <- deserializeDepth - 1

    // --- Public API ---

    /// Serialize a validated program, function dependency names, and source hash into .jyrx binary format.
    let serialize (program: Program) (functionNames: string list) (sourceHash: byte[]) : byte[] =
        use ms = new MemoryStream()
        use w = new BinaryWriter(ms, Encoding.UTF8, true)

        // Header
        w.Write(Encoding.ASCII.GetBytes(MagicBytes))
        w.Write(FormatVersion)
        w.Write(0us) // flags reserved
        w.Write(sourceHash) // 32 bytes SHA256
        w.Write(uint32 functionNames.Length)

        // Function table
        functionNames |> List.iter (writeString w)

        // AST payload
        writeList w writeStmt program.Statements

        w.Flush()
        ms.ToArray()

    /// Deserialize a .jyrx binary into the program AST, required function names, and source hash.
    let deserialize (data: byte[]) : JyroResult<DeserializedProgram> =
        if data.Length > MaxFileSize then
            JyroResult<DeserializedProgram>.Failure(
                DiagnosticMessage.Error(MessageCode.UnknownParserError,
                    sprintf "File size %d exceeds maximum of %d bytes" data.Length MaxFileSize))
        else
        try
            deserializeDepth <- 0
            use ms = new MemoryStream(data)
            use r = new BinaryReader(ms, Encoding.UTF8, true)

            // Header
            let magic = Encoding.ASCII.GetString(r.ReadBytes(4))
            if magic <> MagicBytes then
                JyroResult<DeserializedProgram>.Failure(
                    DiagnosticMessage.Error(MessageCode.UnknownParserError,
                        sprintf "Invalid .jyrx file: expected magic bytes 'JYRX', got '%s'" magic))
            else

            let version = r.ReadUInt16()
            let _flags = r.ReadUInt16()
            let sourceHash = r.ReadBytes(32)
            let funcCount = int (r.ReadUInt32())
            if funcCount > MaxFunctionCount then
                failwithf "Function count %d exceeds maximum of %d" funcCount MaxFunctionCount

            // Function table
            let functionNames = [ for _ in 1 .. funcCount -> readString r ]

            // AST payload
            let statements = readList r readStmt
            let program = { Statements = statements }

            let result =
                { Program = program
                  RequiredFunctions = functionNames
                  SourceHash = sourceHash
                  Version = int version }

            JyroResult<DeserializedProgram>.Success(result)
        with ex ->
            JyroResult<DeserializedProgram>.Failure(
                DiagnosticMessage.Error(MessageCode.UnknownParserError,
                    sprintf "Failed to deserialize .jyrx file: %s" ex.Message))

    /// Read only the header and function table from a .jyrx binary, without deserializing the AST.
    /// Use this for pre-flight validation (hash comparison, function availability checks) without
    /// paying the cost of full AST deserialization.
    let readHeader (data: byte[]) : JyroResult<JyrxHeader> =
        try
            use ms = new MemoryStream(data)
            use r = new BinaryReader(ms, Encoding.UTF8, true)

            let magic = Encoding.ASCII.GetString(r.ReadBytes(4))
            if magic <> MagicBytes then
                JyroResult<JyrxHeader>.Failure(
                    DiagnosticMessage.Error(MessageCode.UnknownParserError,
                        sprintf "Invalid .jyrx file: expected magic bytes 'JYRX', got '%s'" magic))
            else

            let version = r.ReadUInt16()
            let _flags = r.ReadUInt16()
            let sourceHash = r.ReadBytes(32)
            let funcCount = int (r.ReadUInt32())
            let functionNames = [ for _ in 1 .. funcCount -> readString r ]

            JyroResult<JyrxHeader>.Success(
                { Version = int version
                  SourceHash = sourceHash
                  RequiredFunctions = functionNames })
        with ex ->
            JyroResult<JyrxHeader>.Failure(
                DiagnosticMessage.Error(MessageCode.UnknownParserError,
                    sprintf "Failed to read .jyrx header: %s" ex.Message))
