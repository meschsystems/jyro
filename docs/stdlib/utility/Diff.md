# Diff

Compares two objects and returns a structured summary of their differences.

## Signature

```
Diff(object obj1, object obj2)
```

## Parameters

- **obj1** (object): The original object.
- **obj2** (object): The updated object.

## Returns

- **object**: An object with three properties:
  - **added** — properties present in obj2 but not in obj1 (values from obj2)
  - **removed** — properties present in obj1 but not in obj2 (values from obj1)
  - **changed** — properties in both but with different values, each as `{from, to}`

## Description

Performs a shallow (top-level) comparison of two objects. Nested objects and arrays are compared using deep equality, but reported as atomic `{from, to}` pairs when they differ. To diff nested objects, call `Diff` again on the inner values.

Although at the type level, `null` is never equal to `null`, `Diff()` performs a semantic comparison of null values: two `null` values for the same key are treated as equal (no change reported).

When the two objects are identical, the result contains three empty objects: `{added: {}, removed: {}, changed: {}}`.

## Examples

```jyro
var old = {name: "Alice", age: 30, city: "NYC"}
var updated = {name: "Alice", age: 31, email: "a@b.com"}
var d = Diff(old, updated)
# d.added   = {email: "a@b.com"}
# d.removed = {city: "NYC"}
# d.changed = {age: {from: 30, to: 31}}

# Drill into nested changes
var s1 = {user: {name: "Alice", role: "admin"}}
var s2 = {user: {name: "Alice", role: "editor"}}
var d2 = Diff(s1.user, s2.user)
# d2.changed = {role: {from: "admin", to: "editor"}}
```
