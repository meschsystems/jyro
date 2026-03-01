# ValidateRequired

Checks whether an object contains all required fields with non-null, non-empty values.

## Signature

```
ValidateRequired(object obj, array fields)
```

## Parameters

- **obj** (object): The object to validate.
- **fields** (array): An array of field name strings that must be present and non-empty.

## Returns

- **object**: A validation result with properties:
  - **valid** (boolean): `true` if all required fields are present and non-empty.
  - **errors** (array): An array of error message strings describing which fields failed.

## Description

Iterates the required field names and checks each against the object. A field fails validation if it is missing from the object, if its value is null, or if its value is a `string` that is empty or contains only whitespace. Non-string values (numbers, booleans, objects, arrays) pass as long as they exist and are not null. Non-string elements in the `fields` array are skipped.

## Examples

```jyro
var user = { "name": "Alice", "email": "alice@example.com" }

var result = ValidateRequired(user, ["name", "email", "age"])
# result.valid = false
# result.errors = ["Missing required field: age"]

var ok = ValidateRequired(user, ["name", "email"])
# ok.valid = true
# ok.errors = []

# Null values fail
var partial = { "name": "Alice", "age": null }
var check = ValidateRequired(partial, ["name", "age"])
# check.valid = false
# check.errors = ["Field is null: age"]

# Empty and whitespace-only strings fail
var form = { "name": "  ", "email": "" }
var check2 = ValidateRequired(form, ["name", "email"])
# check2.valid = false
# check2.errors = ["Field is empty: name", "Field is empty: email"]
```
