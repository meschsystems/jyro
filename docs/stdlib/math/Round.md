# Round

Rounds a number to a specified number of decimal places with configurable tie-breaking.

## Signature

```
Round(number value, number decimals, string mode?)
```

## Parameters

- **value** (number): The number to round.
- **decimals** (number): The number of decimal places. Use 0 for integer rounding. Negative values round to powers of 10.
- **mode** (string, optional): The rounding strategy for midpoint values. Defaults to `"halfUp"`.

### Rounding Modes

| Mode | Behaviour | Use case |
|------|-----------|----------|
| `"halfUp"` | 2.5 &rarr; 3, 3.5 &rarr; 4 | Consumer finance, general purpose (default) |
| `"halfEven"` | 2.5 &rarr; 2, 3.5 &rarr; 4 | Banker's rounding, regulatory/accounting |
| `"halfDown"` | 2.5 &rarr; 2, 3.5 &rarr; 3 | Some industry sectors |

## Returns

- **number**: The rounded value.

## Description

Rounds a number to the nearest value at a specified decimal precision. When a value falls exactly on the midpoint between two rounded values, the **mode** parameter controls which direction the value is rounded.

Unlike `Floor` and `Ceiling`, which always round in a fixed direction and operate only on integers, `Round` targets the *nearest* value at any precision level, with configurable tie-breaking. Making **decimals** required keeps the intent explicit at every call site.

Negative **decimals** values round to powers of 10. For example, `-1` rounds to the nearest 10, `-2` to the nearest 100, and so on.

## Examples

```jyro
# Basic rounding (halfUp default)
var a = Round(3.456, 2)
# a = 3.46

# Integer rounding
var b = Round(2.5, 0)
# b = 3

# Banker's rounding (halfEven)
var c = Round(2.5, 0, "halfEven")
# c = 2

var d = Round(3.5, 0, "halfEven")
# d = 4

# halfDown mode
var e = Round(2.5, 0, "halfDown")
# e = 2

# Negative decimals (round to nearest 100)
var f = Round(1234.5, -2)
# f = 1200
```
