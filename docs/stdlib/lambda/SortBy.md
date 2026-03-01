# SortBy

Sorts an array using a lambda key extractor.

## Signature

```
SortBy(array arr, lambda fn)
```

## Parameters

- **arr** (array): The array to sort.
- **fn** (lambda): A lambda `(item) => sortKey` that returns a number or string to sort by.

## Returns

- **array**: A new sorted array.

## Description

Creates a new sorted array by comparing the keys returned by the lambda. The lambda should return a number or string for each element. Numbers are compared numerically; strings are compared lexicographically. The original array is not modified.

For field-based sorting on object arrays, see `SortByField`.

## Examples

```jyro
var users = [
    { "name": "Carol", "age": 35 },
    { "name": "Alice", "age": 30 },
    { "name": "Bob", "age": 25 }
]

# Sort by age
var byAge = SortBy(users, (u) => u.age)
# byAge = [Bob (25), Alice (30), Carol (35)]

# Sort by name
var byName = SortBy(users, (u) => u.name)
# byName = [Alice, Bob, Carol]

# Sort numbers by absolute value
var numbers = [-5, 3, -1, 4]
var byAbs = SortBy(numbers, (x) => Absolute(x))
# byAbs = [-1, 3, 4, -5]
```
