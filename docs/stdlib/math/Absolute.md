# Absolute

Returns the absolute value of a number.

## Signature

```
Absolute(number value)
```

## Parameters

- **value** (number): The number to take the absolute value of.

## Returns

- **number**: The non-negative absolute value of the input.

## Description

Computes the absolute value using `Math.Abs`. Negative values are returned as their positive equivalent. Positive values and zero are returned unchanged.

## Examples

```jyro
var result = Absolute(-42)
# result = 42

var zero = Absolute(0)
# zero = 0

var positive = Absolute(3.14)
# positive = 3.14

var negativeDecimal = Absolute(-0.5)
# negativeDecimal = 0.5
```
