namespace Mesch.Jyro

open System
open FParsec.Primitives
open FParsec.CharParsers

/// Token parsing utilities for the Jyro language
module Lexer =
    // Whitespace and comments (Jyro uses # for line comments)
    let private lineComment : Parser<unit, unit> = pchar '#' >>. skipRestOfLine true

    /// Skip whitespace and comments
    let ws : Parser<unit, unit> = skipMany (skipSatisfy Char.IsWhiteSpace <|> lineComment)

    /// Skip at least one whitespace/comment
    let ws1 : Parser<unit, unit> = skipMany1 (skipSatisfy Char.IsWhiteSpace <|> lineComment)

    /// Parse with trailing whitespace
    let lexeme p = p .>> ws

    // Keywords
    let private keywords = Set.ofList [
        "var"; "if"; "then"; "elseif"; "else"; "end"; "switch"; "do"; "case"; "default";
        "while"; "for"; "foreach"; "in"; "return"; "fail"; "break"; "continue";
        "and"; "or"; "not"; "is"; "true"; "false"; "null";
        "number"; "string"; "boolean"; "object"; "array";
        "to"; "downto"; "by"; "func"; "exit";
        "union"; "match"
    ]

    /// Check if a string is a keyword
    let isKeyword s = keywords.Contains(s)

    /// Parse a specific keyword (uses attempt to ensure backtracking on partial matches)
    let keyword kw = attempt (lexeme (pstring kw .>> notFollowedBy (satisfy (fun c -> Char.IsLetterOrDigit(c) || c = '_'))))

    // Identifiers
    let private identifierStart = satisfy (fun c -> Char.IsLetter(c) || c = '_')
    let private identifierChar = satisfy (fun c -> Char.IsLetterOrDigit(c) || c = '_')

    /// Parse an identifier (not a keyword)
    let identifier =
        lexeme (
            many1Chars2 identifierStart identifierChar
            >>= fun s ->
                if isKeyword s then fail (sprintf "'%s' is a reserved keyword" s)
                else preturn s
        )

    /// Parse any identifier including keywords (for type names)
    let anyIdentifier =
        lexeme (many1Chars2 identifierStart identifierChar)

    // Literals
    /// Parse a number literal
    let numberLiteral =
        lexeme (
            opt (pchar '-') .>>. (
                attempt (pstring "0x" >>. many1Chars hex |>> fun s -> float (Convert.ToInt64(s, 16))) <|>
                attempt (pstring "0b" >>. many1Chars (satisfy (fun c -> c = '0' || c = '1')) |>> fun s -> float (Convert.ToInt64(s, 2))) <|>
                pfloat
            ) |>> fun (neg, n) -> if neg.IsSome then -n else n
        )

    /// Parse escape sequences in strings
    let private escapedChar =
        pchar '\\' >>. (
            (pchar 'n' >>% '\n') <|>
            (pchar 'r' >>% '\r') <|>
            (pchar 't' >>% '\t') <|>
            (pchar '\\' >>% '\\') <|>
            (pchar '"' >>% '"') <|>
            (pchar '\'' >>% '\'') <|>
            (pchar '0' >>% '\000') <|>
            (pchar 'u' >>. manyMinMaxSatisfy 4 4 isHex |>> fun s -> char (Convert.ToInt32(s, 16)))
        )

    /// Parse a double-quoted string literal
    let stringLiteral =
        lexeme (
            between (pchar '"') (pchar '"') (
                manyChars (escapedChar <|> noneOf "\"\\")
            )
        )

    /// Parse a single-quoted string literal
    let stringLiteralSingle =
        lexeme (
            between (pchar '\'') (pchar '\'') (
                manyChars (escapedChar <|> noneOf "'\\")
            )
        )

    /// Parse either string literal type
    let anyStringLiteral = stringLiteral <|> stringLiteralSingle

    /// Parse boolean literal
    let booleanLiteral = (keyword "true" >>% true) <|> (keyword "false" >>% false)

    /// Parse null literal
    let nullLiteral = keyword "null"

    // Operators and punctuation
    let private opChars = "+-*/%=!<>?:."

    /// Parse a specific symbol
    let symbol s = lexeme (pstring s)

    /// Parse a specific symbol not followed by more operator chars
    let symbolOp s = lexeme (pstring s .>> notFollowedBy (anyOf opChars))

    // Common symbols
    let lparen = symbol "("
    let rparen = symbol ")"
    let lbracket = symbol "["
    let rbracket = symbol "]"
    let lbrace = symbol "{"
    let rbrace = symbol "}"
    let comma = symbol ","
    let colon = symbol ":"
    let dot = symbol "."
    let arrow = symbol "=>"
    let questionMark = symbol "?"

    // Assignment operators
    let assignOp =
        choice [
            symbol "+=" >>% AddAssign
            symbol "-=" >>% SubtractAssign
            symbol "*=" >>% MultiplyAssign
            symbol "/=" >>% DivideAssign
            symbol "%=" >>% ModuloAssign
            symbolOp "=" >>% Assign
        ]

    // Comparison and logical operators
    let eqOp = symbol "==" >>% Equal
    let neqOp = symbol "!=" >>% NotEqual
    let leOp = symbol "<=" >>% LessThanOrEqual
    let geOp = symbol ">=" >>% GreaterThanOrEqual
    let ltOp = symbolOp "<" >>% LessThan
    let gtOp = symbolOp ">" >>% GreaterThan
    let andOp = keyword "and" >>% And
    let orOp = keyword "or" >>% Or
    let notOp = keyword "not"
    let coalesceOp = symbol "??" >>% Coalesce

    // Arithmetic operators
    let addOp = symbolOp "+" >>% Add
    let subOp = symbolOp "-" >>% Subtract
    let mulOp = symbolOp "*" >>% Multiply
    let divOp = symbolOp "/" >>% Divide
    let modOp = symbolOp "%" >>% Modulo

    // Increment/decrement
    let incrementOp = symbol "++"
    let decrementOp = symbol "--"

    // Type keywords
    let typeKeyword =
        choice [
            keyword "number" >>% NumberType
            keyword "string" >>% StringType
            keyword "boolean" >>% BooleanType
            keyword "object" >>% ObjectType
            keyword "array" >>% ArrayType
            keyword "null" >>% NullType
        ]

    /// Initialize the parser with leading whitespace
    let initParser : Parser<unit, unit> = ws
