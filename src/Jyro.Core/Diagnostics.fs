namespace Mesch.Jyro

open System
open System.Collections.Generic

/// Severity level of a diagnostic message
type MessageSeverity =
    | Info = 0
    | Warning = 1
    | Error = 2

/// Diagnostic message codes organized by compilation stage.
/// Pattern: XYZZ where X=pipeline stage, Y=category, ZZ=specific error.
/// Googleable as JMXXXX (e.g. JM5200 = Division by zero).
type MessageCode =
    // Lexical Analysis Errors (1xxx)
    // 10xx — General
    | UnknownLexerError = 1000
    // 11xx — Character errors
    | UnexpectedCharacter = 1100
    // 12xx — Literal errors
    | UnterminatedString = 1200

    // Parsing Errors (2xxx)
    // 20xx — General
    | UnknownParserError = 2000
    // 21xx — Token errors
    | UnexpectedToken = 2100
    | MissingToken = 2101
    // 22xx — Literal/format errors
    | InvalidNumberFormat = 2200

    // Validation Errors (3xxx)
    // 30xx — General
    | UnknownValidatorError = 3000
    // 31xx — Variable declaration
    | UndeclaredVariable = 3100
    | VariableAlreadyDeclared = 3101
    | ReservedIdentifier = 3102
    // 32xx — Assignment
    | InvalidAssignmentTarget = 3200
    // 33xx — Type
    | TypeMismatch = 3300
    // 34xx — Control flow
    | LoopStatementOutsideOfLoop = 3400
    | ExcessiveLoopNesting = 3401
    | NotIterableLiteral = 3402
    | UnreachableCode = 3403

    // Linking Errors (4xxx)
    // 40xx — General
    | UnknownLinkerError = 4000
    // 41xx — Function resolution
    | UndefinedFunction = 4100
    | DuplicateFunction = 4101
    | FunctionOverride = 4102
    // 42xx — Argument validation
    | TooFewArguments = 4200
    | TooManyArguments = 4201

    // Execution / Runtime Errors (5xxx)
    // 50xx — General / System
    | ScriptReturn = 5000
    | UnknownExecutorError = 5001
    | RuntimeError = 5002
    | CancelledByHost = 5003
    | ScriptFailure = 5004

    // 51xx — Type & Casting
    | InvalidType = 5100
    | InvalidArgumentType = 5101
    | InvalidCast = 5102
    | ArgumentNotProvided = 5103
    | ArgumentTypeMismatch = 5104
    | CallbackExpected = 5105

    // 52xx — Arithmetic & Operators
    | DivisionByZero = 5200
    | ModuloByZero = 5201
    | NegateNonNumber = 5202
    | IncrementDecrementNonNumber = 5203
    | IncompatibleOperandTypes = 5204
    | IncompatibleComparison = 5205
    | UnsupportedBinaryOperation = 5206
    | UnsupportedUnaryOperation = 5207
    | UnknownOperator = 5208

    // 53xx — Index & Property Access
    | IndexOutOfRange = 5300
    | NegativeIndex = 5301
    | IndexAccessOnNull = 5302
    | InvalidIndexTarget = 5303
    | PropertyAccessOnNull = 5304
    | PropertyAccessInvalidType = 5305
    | SetPropertyOnNonObject = 5306
    | SetIndexOnNonContainer = 5307

    // 54xx — Type Checking & Iteration
    | InvalidTypeCheck = 5400
    | UnknownTypeName = 5401
    | NotIterable = 5402

    // 55xx — Function & Script Resolution
    | UndefinedFunctionRuntime = 5500
    | InvalidFunctionTarget = 5501
    | ScriptResolverNotConfigured = 5502
    | ScriptNotFound = 5503

    // 56xx — Control Flow & Expression
    | InvalidExpressionSyntax = 5600
    | InvalidNumberParse = 5601

    // 57xx — String & Pattern Operations (stdlib)
    | RegexTimeout = 5700
    | RegexInvalidPattern = 5701
    | PadLengthExceeded = 5702
    | EmptyCharacterSet = 5703
    | StringOrArrayRequired = 5704

    // 58xx — Date & Time Operations (stdlib)
    | DateParseError = 5800
    | DateFormatStringInvalid = 5801
    | DateFormatInvalid = 5802
    | DateAddAmountNotInteger = 5803
    | DateUnitInvalid = 5804
    | DatePartInvalid = 5805

    // 59xx — Resource Limits, Encoding & Collection Operations
    | StatementLimitExceeded = 5900
    | LoopIterationLimitExceeded = 5901
    | CallDepthLimitExceeded = 5902
    | ExecutionTimeLimitExceeded = 5903
    | IntegerRequired = 5904
    | IncomparableTypes = 5905
    | UnsupportedComparisonOperator = 5906
    | NonNegativeIntegerRequired = 5907
    | Base64DecodeError = 5908

/// Source location information
[<Struct>]
type SourceLocation =
    { Line: int
      Column: int
      Length: int }

    static member None = { Line = 0; Column = 0; Length = 0 }

    static member Create(line: int, column: int, ?length: int) =
        { Line = line; Column = column; Length = defaultArg length 0 }

/// A diagnostic message from compilation or execution
type DiagnosticMessage =
    { Code: MessageCode
      Severity: MessageSeverity
      Message: string
      Args: obj[]
      Location: SourceLocation option }

    static member Error(code: MessageCode, message: string, ?args: obj[], ?location: SourceLocation) =
        { Code = code
          Severity = MessageSeverity.Error
          Message = message
          Args = defaultArg args Array.empty
          Location = location }

    static member Warning(code: MessageCode, message: string, ?args: obj[], ?location: SourceLocation) =
        { Code = code
          Severity = MessageSeverity.Warning
          Message = message
          Args = defaultArg args Array.empty
          Location = location }

    static member Info(code: MessageCode, message: string, ?args: obj[], ?location: SourceLocation) =
        { Code = code
          Severity = MessageSeverity.Info
          Message = message
          Args = defaultArg args Array.empty
          Location = location }

/// Result of a compilation or execution phase
type JyroResult<'T> =
    { Value: 'T option
      Messages: DiagnosticMessage list
      IsSuccess: bool }

    static member Success(value: 'T, ?messages: DiagnosticMessage list) =
        { Value = Some value
          Messages = defaultArg messages []
          IsSuccess = true }

    static member Failure(messages: DiagnosticMessage list) =
        { Value = None
          Messages = messages
          IsSuccess = false }

    static member Failure(message: DiagnosticMessage) =
        { Value = None
          Messages = [ message ]
          IsSuccess = false }

    member this.HasErrors =
        this.Messages |> List.exists (fun m -> m.Severity = MessageSeverity.Error)

/// Interface for hosts to provide localized message templates.
/// Return None for a code to fall back to the default English message.
type IMessageTemplateProvider =
    abstract member GetTemplate: MessageCode -> string option

/// Default English message templates keyed by MessageCode.
/// Each template uses {0}, {1}, etc. for positional arguments.
module MessageTemplates =
    let private templates = Dictionary<MessageCode, string>()

    do
        // Lexer
        templates.[MessageCode.UnknownLexerError] <- "{0}"
        templates.[MessageCode.UnexpectedCharacter] <- "Unexpected character '{0}'"
        templates.[MessageCode.UnterminatedString] <- "Unterminated string literal"

        // Parser
        templates.[MessageCode.UnknownParserError] <- "{0}"
        templates.[MessageCode.UnexpectedToken] <- "Unexpected token '{0}'"
        templates.[MessageCode.MissingToken] <- "Expected '{0}'"
        templates.[MessageCode.InvalidNumberFormat] <- "Invalid number format: '{0}'"

        // Validator
        templates.[MessageCode.UnknownValidatorError] <- "{0}"
        templates.[MessageCode.UndeclaredVariable] <- "Undeclared variable '{0}'"
        templates.[MessageCode.VariableAlreadyDeclared] <- "Variable '{0}' is already declared"
        templates.[MessageCode.ReservedIdentifier] <- "'{0}' is a reserved identifier"
        templates.[MessageCode.InvalidAssignmentTarget] <- "Invalid assignment target"
        templates.[MessageCode.TypeMismatch] <- "{0}"
        templates.[MessageCode.LoopStatementOutsideOfLoop] <- "{0} statement outside of loop"
        templates.[MessageCode.ExcessiveLoopNesting] <- "Loop nesting exceeds maximum depth of {0}"
        templates.[MessageCode.UnreachableCode] <- "Unreachable code detected"
        templates.[MessageCode.NotIterableLiteral] <- "Value of type {0} is not iterable"

        // Linker
        templates.[MessageCode.UnknownLinkerError] <- "{0}"
        templates.[MessageCode.UndefinedFunction] <- "Undefined function '{0}'"
        templates.[MessageCode.DuplicateFunction] <- "Function '{0}' is already defined"
        templates.[MessageCode.FunctionOverride] <- "Function '{0}' overrides a built-in function"
        templates.[MessageCode.TooFewArguments] <- "Function '{0}' requires at least {1} arguments, but {2} were provided"
        templates.[MessageCode.TooManyArguments] <- "Function '{0}' accepts at most {1} arguments, but {2} were provided"

        // Runtime — General
        templates.[MessageCode.UnknownExecutorError] <- "{0}"
        templates.[MessageCode.RuntimeError] <- "{0}"
        templates.[MessageCode.CancelledByHost] <- "Script execution was cancelled by the host"
        templates.[MessageCode.ScriptFailure] <- "{0}"

        // Runtime — Type & Casting
        templates.[MessageCode.InvalidType] <- "Cannot assign {0} to variable '{1}' of type {2}"
        templates.[MessageCode.InvalidArgumentType] <- "{0}"
        templates.[MessageCode.InvalidCast] <- "Cannot cast {0} to {1}"
        templates.[MessageCode.ArgumentNotProvided] <- "Argument {0} not provided"
        templates.[MessageCode.ArgumentTypeMismatch] <- "Expected {0} but got {1}"
        templates.[MessageCode.CallbackExpected] <- "{0} expected a lambda callback but got {1}"

        // Runtime — Arithmetic & Operators
        templates.[MessageCode.DivisionByZero] <- "Division by zero"
        templates.[MessageCode.ModuloByZero] <- "Modulo by zero"
        templates.[MessageCode.NegateNonNumber] <- "Cannot negate non-number value of type {0}"
        templates.[MessageCode.IncrementDecrementNonNumber] <- "Cannot increment/decrement non-number value of type {0}"
        templates.[MessageCode.IncompatibleOperandTypes] <- "Incompatible operand types: {0} and {1}"
        templates.[MessageCode.IncompatibleComparison] <- "Cannot compare {0} and {1}"
        templates.[MessageCode.UnsupportedBinaryOperation] <- "Unsupported binary operation {0} for {1}"
        templates.[MessageCode.UnsupportedUnaryOperation] <- "Unsupported unary operation {0} for {1}"
        templates.[MessageCode.UnknownOperator] <- "Unknown operator '{0}'"

        // Runtime — Index & Property Access
        templates.[MessageCode.IndexOutOfRange] <- "{0}"
        templates.[MessageCode.NegativeIndex] <- "Negative index: {0}"
        templates.[MessageCode.IndexAccessOnNull] <- "Cannot access index on null value"
        templates.[MessageCode.InvalidIndexTarget] <- "Cannot index into value of type {0}"
        templates.[MessageCode.PropertyAccessOnNull] <- "Cannot access property '{0}' on null value"
        templates.[MessageCode.PropertyAccessInvalidType] <- "Cannot access property '{0}' on value of type {1}"
        templates.[MessageCode.SetPropertyOnNonObject] <- "Cannot set property '{0}' on value of type {1}"
        templates.[MessageCode.SetIndexOnNonContainer] <- "Cannot set index on value of type {0}"

        // Runtime — Type Checking & Iteration
        templates.[MessageCode.InvalidTypeCheck] <- "{0}"
        templates.[MessageCode.UnknownTypeName] <- "Unknown type name '{0}'"
        templates.[MessageCode.NotIterable] <- "Value of type {0} is not iterable"

        // Runtime — Function & Script Resolution
        templates.[MessageCode.UndefinedFunctionRuntime] <- "Undefined function '{0}'"
        templates.[MessageCode.InvalidFunctionTarget] <- "Cannot call value of type {0} as a function"
        templates.[MessageCode.ScriptResolverNotConfigured] <- "Script resolver is not configured"
        templates.[MessageCode.ScriptNotFound] <- "Script '{0}' not found"

        // Runtime — Control Flow & Expression
        templates.[MessageCode.InvalidExpressionSyntax] <- "{0}"
        templates.[MessageCode.InvalidNumberParse] <- "Cannot parse '{0}' as a number"

        // Runtime — String & Pattern Operations
        templates.[MessageCode.RegexTimeout] <- "{0}: Pattern matching timed out"
        templates.[MessageCode.RegexInvalidPattern] <- "{0}: Invalid regex pattern - {1}"
        templates.[MessageCode.PadLengthExceeded] <- "{0} length {1} exceeds maximum allowed value of {2}"
        templates.[MessageCode.EmptyCharacterSet] <- "{0} character set cannot be empty"
        templates.[MessageCode.StringOrArrayRequired] <- "{0} requires a string or array as first argument"

        // Runtime — Date & Time Operations
        templates.[MessageCode.DateParseError] <- "Unable to parse date: '{0}'"
        templates.[MessageCode.DateFormatStringInvalid] <- "Invalid date format string: '{0}'"
        templates.[MessageCode.DateFormatInvalid] <- "Invalid date format: '{0}'"
        templates.[MessageCode.DateAddAmountNotInteger] <- "DateAdd() amount must be an integer"
        templates.[MessageCode.DateUnitInvalid] <- "Invalid date unit: '{0}'. Valid units: {1}"
        templates.[MessageCode.DatePartInvalid] <- "Invalid date part: '{0}'. Valid parts: {1}"

        // Runtime — Resource Limits, Encoding & Collection
        templates.[MessageCode.StatementLimitExceeded] <- "Script execution exceeded maximum statement limit of {0}"
        templates.[MessageCode.LoopIterationLimitExceeded] <- "Script execution exceeded maximum loop iteration limit of {0}"
        templates.[MessageCode.CallDepthLimitExceeded] <- "Script execution exceeded maximum call depth limit of {0}"
        templates.[MessageCode.ExecutionTimeLimitExceeded] <- "Script execution exceeded maximum time limit of {0}ms"
        templates.[MessageCode.IntegerRequired] <- "{0} requires an integer {1}. Received: {2}"
        templates.[MessageCode.IncomparableTypes] <- "Cannot compare values of incompatible types: {0} and {1}. Relational operators (<, <=, >, >=) require both values to be numbers, strings, or booleans of the same type."
        templates.[MessageCode.UnsupportedComparisonOperator] <- "Unsupported comparison operator: '{0}'. Supported operators are: ==, !=, <, <=, >, >="
        templates.[MessageCode.NonNegativeIntegerRequired] <- "{0} requires {1}. Received: {2}"
        templates.[MessageCode.Base64DecodeError] <- "Base64Decode() requires a valid Base64-encoded string. Error: {0}"

    /// Gets the default English template for a message code, or "{0}" if not found.
    let get (code: MessageCode) : string =
        match templates.TryGetValue(code) with
        | true, t -> t
        | _ -> "{0}"

    /// Gets the default English template as an option.
    let tryGet (code: MessageCode) : string option =
        match templates.TryGetValue(code) with
        | true, t -> Some t
        | _ -> None

    /// All default templates as a read-only dictionary.
    let defaults : IReadOnlyDictionary<MessageCode, string> = templates

/// Structured diagnostic for programmatic consumption by embedded hosts (JSON APIs, UI, etc.)
type StructuredDiagnostic =
    { Code: string
      NumericCode: int
      Severity: string
      Message: string
      Args: obj[]
      Line: int
      Column: int
      Length: int
      Subsystem: string }

/// Formatting utilities for diagnostic messages.
module DiagnosticFormatter =
    /// Map a numeric code range to its pipeline subsystem name.
    let private codeToSubsystem (code: int) : string =
        match code with
        | c when c >= 1000 && c < 2000 -> "lexer"
        | c when c >= 2000 && c < 3000 -> "parser"
        | c when c >= 3000 && c < 4000 -> "validator"
        | c when c >= 4000 && c < 5000 -> "linker"
        | c when c >= 5000 && c < 6000 -> "runtime"
        | _ -> "unknown"

    /// Format a MessageCode as "JMXXXX" (e.g. JM5200).
    let formatCode (code: MessageCode) : string =
        sprintf "JM%04d" (int code)

    /// Format a DiagnosticMessage as "[JMXXXX] Ln N, Col N: message".
    let formatMessage (msg: DiagnosticMessage) : string =
        let codeStr = formatCode msg.Code
        match msg.Location with
        | Some loc when loc.Line > 0 ->
            sprintf "[%s] Ln %d, Col %d: %s" codeStr loc.Line loc.Column msg.Message
        | _ ->
            sprintf "[%s]: %s" codeStr msg.Message

    /// Format a DiagnosticMessage using a host-provided template provider for localization.
    /// Falls back to the pre-formatted English message if no template is found.
    let formatLocalized (provider: IMessageTemplateProvider) (msg: DiagnosticMessage) : string =
        let codeStr = formatCode msg.Code
        let message =
            match provider.GetTemplate(msg.Code) with
            | Some template when msg.Args.Length > 0 -> String.Format(template, msg.Args)
            | Some template -> template
            | None -> msg.Message
        match msg.Location with
        | Some loc when loc.Line > 0 ->
            sprintf "[%s] Ln %d, Col %d: %s" codeStr loc.Line loc.Column message
        | _ ->
            sprintf "[%s]: %s" codeStr message

    /// Convert a DiagnosticMessage to a structured record for JSON serialization.
    let toStructured (msg: DiagnosticMessage) : StructuredDiagnostic =
        let numCode = int msg.Code
        { Code = formatCode msg.Code
          NumericCode = numCode
          Severity = msg.Severity.ToString().ToLowerInvariant()
          Message = msg.Message
          Args = msg.Args
          Line = msg.Location |> Option.map (fun l -> l.Line) |> Option.defaultValue 0
          Column = msg.Location |> Option.map (fun l -> l.Column) |> Option.defaultValue 0
          Length = msg.Location |> Option.map (fun l -> l.Length) |> Option.defaultValue 0
          Subsystem = codeToSubsystem numCode }

