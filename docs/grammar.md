# Jyro Language Grammar

Formal grammar specification for the Jyro scripting language, expressed in Extended Backus-Naur Form (EBNF).

## Notation

| Symbol     | Meaning                                |
|------------|----------------------------------------|
| `=`        | Definition                             |
| `;`        | End of rule                            |
| `\|`       | Alternative                            |
| `,`        | Concatenation                          |
| `{ ... }`  | Repetition (zero or more)              |
| `[ ... ]`  | Optional (zero or one)                 |
| `( ... )`  | Grouping                               |
| `" ... "`  | Terminal string                        |
| `' ... '`  | Terminal string (alternate quoting)    |
| `(* ... *)` | Comment                               |

---

## 1. Lexical Grammar

### 1.1 Whitespace and Comments

```ebnf
whitespace = ? any Unicode whitespace character ? ;
line_comment = "#" , { ? any character except newline ? } ;
ws = { whitespace | line_comment } ;
```

### 1.2 Identifiers

```ebnf
letter = "A" | "B" | ... | "Z" | "a" | "b" | ... | "z" ;
digit = "0" | "1" | ... | "9" ;
identifier_start = letter | "_" ;
identifier_char = letter | digit | "_" ;

identifier = identifier_start , { identifier_char } ;  (* must not be a keyword *)
```

### 1.3 Keywords

The following words are reserved and cannot be used as identifiers:

```
var       if        then      elseif    else      end
switch    do        case      default   while     for
foreach   in        return    fail      break     continue
and       or        not       is        true      false
null      number    string    boolean   object    array
to        downto    by        func      exit      union
match
```

### 1.4 Literals

#### Number Literals

```ebnf
hex_digit = digit | "A" | "B" | "C" | "D" | "E" | "F"
                   | "a" | "b" | "c" | "d" | "e" | "f" ;
binary_digit = "0" | "1" ;

decimal_literal = [ "-" ] , digit , { digit } , [ "." , digit , { digit } ] ;
hex_literal = [ "-" ] , "0x" , hex_digit , { hex_digit } ;
binary_literal = [ "-" ] , "0b" , binary_digit , { binary_digit } ;

number_literal = hex_literal | binary_literal | decimal_literal ;
```

#### String Literals

```ebnf
escape_sequence = "\" , ( "n" | "r" | "t" | "\" | '"' | "'" | "0"
                        | "u" , hex_digit , hex_digit , hex_digit , hex_digit ) ;

double_string = '"' , { escape_sequence | ? any character except '"' and '\' ? } , '"' ;
single_string = "'" , { escape_sequence | ? any character except "'" and '\' ? } , "'" ;

string_literal = double_string | single_string ;
```

#### Boolean and Null Literals

```ebnf
boolean_literal = "true" | "false" ;
null_literal = "null" ;

literal = number_literal | string_literal | boolean_literal | null_literal ;
```

### 1.5 Operators and Punctuation

#### Arithmetic Operators

| Operator | Description    |
|----------|----------------|
| `+`      | Addition       |
| `-`      | Subtraction    |
| `*`      | Multiplication |
| `/`      | Division       |
| `%`      | Modulo         |

#### Comparison Operators

| Operator | Description           |
|----------|-----------------------|
| `==`     | Equal                 |
| `!=`     | Not equal             |
| `<`      | Less than             |
| `<=`     | Less than or equal    |
| `>`      | Greater than          |
| `>=`     | Greater than or equal |

#### Logical Operators

| Operator | Description      |
|----------|------------------|
| `and`    | Logical AND      |
| `or`     | Logical OR       |
| `not`    | Logical NOT      |

#### Assignment Operators

| Operator | Description          |
|----------|----------------------|
| `=`      | Assign               |
| `+=`     | Add and assign       |
| `-=`     | Subtract and assign  |
| `*=`     | Multiply and assign  |
| `/=`     | Divide and assign    |
| `%=`     | Modulo and assign    |

#### Other Operators

| Operator  | Description                              |
|-----------|------------------------------------------|
| `??`      | Null coalescing                          |
| `? :`     | Ternary conditional                      |
| `is`      | Type check                               |
| `is not`  | Negated type check                       |
| `++`      | Increment (prefix or postfix)            |
| `--`      | Decrement (prefix or postfix)            |
| `.`       | Property access                          |
| `[ ]`     | Index access                             |
| `( )`     | Function call / grouping                 |
| `=>`      | Lambda arrow                             |

#### Punctuation

| Symbol | Usage                           |
|--------|---------------------------------|
| `(`    | Open parenthesis                |
| `)`    | Close parenthesis               |
| `[`    | Open bracket                    |
| `]`    | Close bracket                   |
| `{`    | Open brace                      |
| `}`    | Close brace                     |
| `,`    | Separator                       |
| `:`    | Type annotation / object pair   |

---

## 2. Syntactic Grammar

### 2.1 Program Structure

```ebnf
program = ws , { statement } , EOF ;
block = { statement } ;
```

### 2.2 Statements

```ebnf
statement = func_def
          | union_def
          | var_decl
          | if_stmt
          | while_stmt
          | for_stmt
          | foreach_stmt
          | switch_stmt
          | match_stmt
          | return_stmt
          | exit_stmt
          | fail_stmt
          | break_stmt
          | continue_stmt
          | assignment_stmt
          | expr_stmt ;
```

#### Function Definition

```ebnf
func_param = identifier , [ ":" , type_keyword ] ;
func_params = "(" , [ func_param , { "," , func_param } ] , ")" ;

func_def = "func" , identifier , func_params , block , "end" ;
```

Functions must be declared at the top level (not nested inside control flow or other functions). Functions are pure - they cannot access `Data` and have no closures. All dependencies must be passed as parameters.

#### Union Declaration

```ebnf
variant_fields = "(" , [ func_param , { "," , func_param } ] , ")" ;
variant = identifier , [ variant_fields ] ;

union_def = "union" , identifier , variant , { variant } , "end" ;
```

Unions must be declared at the top level (not nested inside control flow or functions). Each variant acts as a constructor function that creates a tagged object with a `_variant` discriminator field. Variant names must be unique across all unions.

#### Match Statement

```ebnf
match_bindings = "(" , [ identifier , { "," , identifier } ] , ")" ;
match_case = "case" , identifier , [ match_bindings ] , "then" , block ;

match_stmt = "match" , expression , "do" ,
             match_case , { match_case } ,
             "end" ;
```

Match destructures a tagged union value by its `_variant` tag. Each case binds the variant's fields to local variables. The match must be exhaustive - all variants of the union must be covered. For open-ended dispatch with a catch-all, use `switch` with `default` instead.

#### Variable Declaration

```ebnf
type_keyword = "number" | "string" | "boolean" | "object" | "array" ;

var_decl = "var" , identifier , [ ":" , type_keyword ] , [ "=" , expression ] ;
```

#### Assignment

```ebnf
assign_op = "=" | "+=" | "-=" | "*=" | "/=" | "%=" ;
assign_target = identifier | property_access | index_access ;

assignment_stmt = assign_target , assign_op , expression ;
```

#### If Statement

```ebnf
if_stmt = "if" , expression , "then" , block ,
          { "elseif" , expression , "then" , block } ,
          [ "else" , block ] ,
          "end" ;
```

#### While Loop

```ebnf
while_stmt = "while" , expression , "do" , block , "end" ;
```

#### For Loop (Range-Based)

```ebnf
for_direction = "to" | "downto" ;

for_stmt = "for" , identifier , "in" , expression ,
           for_direction , expression ,
           [ "by" , expression ] ,
           "do" , block , "end" ;
```

#### ForEach Loop

```ebnf
foreach_stmt = "foreach" , identifier , "in" , expression ,
               "do" , block , "end" ;
```

#### Switch Statement

```ebnf
case_values = expression , { "," , expression } ;

switch_case = "case" , case_values , "then" , block ;
default_case = "default" , "then" , block ;

switch_stmt = "switch" , expression , "do" ,
              { switch_case } ,
              [ default_case ] ,
              "end" ;
```

#### Return Statement

```ebnf
return_stmt = "return" , [ expression ] ;  (* expression must begin on same line; only valid inside func *)
```

#### Exit Statement

```ebnf
exit_stmt = "exit" , [ expression ] ;  (* expression must begin on same line; clean script termination *)
```

#### Fail Statement

```ebnf
fail_stmt = "fail" , [ expression ] ;  (* expression must begin on same line *)
```

#### Break and Continue

```ebnf
break_stmt = "break" ;
continue_stmt = "continue" ;
```

#### Expression Statement

```ebnf
expr_stmt = expression ;
```

### 2.3 Expressions

Expressions are listed below in order of increasing precedence. Each level binds tighter than the one above it.

```ebnf
expression = ternary_expr ;
```

#### Precedence 1: Ternary Conditional (lowest)

```ebnf
ternary_expr = coalesce_expr , [ "?" , expression , ":" , expression ] ;
```

#### Precedence 2: Null Coalescing (right-associative)

```ebnf
coalesce_expr = or_expr , { "??" , or_expr } ;  (* right-associative *)
```

#### Precedence 3: Logical OR

```ebnf
or_expr = and_expr , { "or" , and_expr } ;
```

#### Precedence 4: Logical AND

```ebnf
and_expr = not_expr , { "and" , not_expr } ;
```

#### Precedence 5: Logical NOT (prefix unary)

```ebnf
not_expr = "not" , not_expr
         | type_check_expr ;
```

#### Precedence 6: Type Check

```ebnf
type_check_expr = equality_expr , [ "is" , [ "not" ] , type_keyword ] ;
```

#### Precedence 7: Equality

```ebnf
equality_op = "==" | "!=" ;
equality_expr = relational_expr , { equality_op , relational_expr } ;
```

#### Precedence 8: Relational

```ebnf
relational_op = "<" | "<=" | ">" | ">=" ;
relational_expr = additive_expr , { relational_op , additive_expr } ;
```

#### Precedence 9: Additive

```ebnf
additive_op = "+" | "-" ;
additive_expr = multiplicative_expr , { additive_op , multiplicative_expr } ;
```

#### Precedence 10: Multiplicative

```ebnf
multiplicative_op = "*" | "/" | "%" ;
multiplicative_expr = unary_expr , { multiplicative_op , unary_expr } ;
```

#### Precedence 11: Unary Prefix

```ebnf
unary_expr = "++" , postfix_expr       (* prefix increment *)
           | "--" , postfix_expr       (* prefix decrement *)
           | "-" , unary_expr          (* numeric negation *)
           | postfix_expr ;
```

#### Precedence 12: Postfix

```ebnf
postfix_expr = primary_expr , { postfix_op } ;

postfix_op = "(" , [ expression , { "," , expression } ] , ")"    (* function call *)
           | "." , identifier                                       (* property access *)
           | "[" , expression , "]"                                 (* index access *)
           | "++"                                                   (* postfix increment *)
           | "--" ;                                                 (* postfix decrement *)
```

Note: Function calls are only valid when the base expression is an identifier. The call `Name(args)` invokes the function `Name`.

#### Precedence 13: Primary (highest)

```ebnf
primary_expr = lambda_expr
             | null_literal
             | boolean_literal
             | number_literal
             | string_literal
             | array_literal
             | object_literal
             | match_expr
             | grouped_expr
             | identifier ;
```

#### Composite Literals

```ebnf
array_literal = "[" , [ expression , { "," , expression } ] , "]" ;

object_key = identifier | string_literal ;
object_property = object_key , ":" , expression ;
object_literal = "{" , [ object_property , { "," , object_property } ] , "}" ;
```

#### Lambda Expressions

```ebnf
lambda_params = "(" , [ identifier , { "," , identifier } ] , ")"
              | identifier ;

lambda_expr = lambda_params , "=>" , expression ;
```

#### Match Expression

```ebnf
match_expr_case = "case" , identifier , [ match_bindings ] , "then" , expression ;

match_expr = "match" , expression , "do" ,
             match_expr_case , { match_expr_case } ,
             "end" ;
```

Match expressions produce a value. Each case arm is a single expression (not a block). Like statement match, the match expression must be exhaustive - all variants of the union must be covered.

#### Grouped Expression

```ebnf
grouped_expr = "(" , expression , ")" ;
```

---

## 3. Operator Precedence Summary

From lowest to highest precedence:

| Prec. | Operator(s)                     | Associativity | Description           |
|------:|---------------------------------|---------------|-----------------------|
|     1 | `? :`                           | Right         | Ternary conditional   |
|     2 | `??`                            | Right         | Null coalescing       |
|     3 | `or`                            | Left          | Logical OR            |
|     4 | `and`                           | Left          | Logical AND           |
|     5 | `not`                           | Prefix        | Logical NOT           |
|     6 | `is` , `is not`                 | Postfix       | Type check            |
|     7 | `==` , `!=`                     | Left          | Equality              |
|     8 | `<` , `<=` , `>` , `>=`         | Left          | Relational            |
|     9 | `+` , `-`                       | Left          | Additive              |
|    10 | `*` , `/` , `%`                 | Left          | Multiplicative        |
|    11 | `-` , `++` , `--`               | Prefix        | Unary prefix          |
|    12 | `()` , `.` , `[]` , `++` , `--` | Left/Postfix  | Postfix / access      |

---

## 4. Type System

Jyro supports the following runtime types. Type keywords are used in variable declarations and `is` expressions.

| Type Keyword | Values                                     |
|--------------|--------------------------------------------|
| `number`     | IEEE 754 double-precision floating point    |
| `string`     | Unicode character sequence                  |
| `boolean`    | `true` or `false`                           |
| `object`     | Key-value map with string keys              |
| `array`      | Ordered, indexable collection of values     |
| `null`       | The singleton `null` value                  |
