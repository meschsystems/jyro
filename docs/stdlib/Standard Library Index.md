# Jyro Standard Library Reference

A complete reference of all built-in functions available in the Jyro scripting language.

**Immutability:** Jyro follows an immutable-by-default design. Functions return new values rather than modifying their inputs in place. Your original data is never altered.

**Chaining:** Functions that return arrays, strings, or objects can be chained by nesting calls or using the result as input to another function. Functions that return scalars (numbers, booleans) or null are terminal and cannot be meaningfully chained.

---

## Array Functions

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [Append](array/Append.md) | Adds an element to the end of an array | No, returns new array | Yes |
| [Concatenate](array/Concatenate.md) | Joins two arrays into a single new array | No, returns new array | Yes |
| [Distinct](array/Distinct.md) | Removes duplicate elements, keeping first occurrence | No, returns new array | Yes |
| [First](array/First.md) | Returns the first element, or null if empty | No | No |
| [Flatten](array/Flatten.md) | Recursively flattens nested arrays into a single level | No, returns new array | Yes |
| [FlattenOnce](array/FlattenOnce.md) | Flattens nested arrays by one level only | No, returns new array | Yes |
| [GroupBy](array/GroupBy.md) | Groups array elements by a field value into an object | No, returns new object | Yes (object) |
| [IndexOf](array/IndexOf.md) | Returns the position of a value in an array or string | No | No |
| [Insert](array/Insert.md) | Inserts an element at a specified index | No, returns new array | Yes |
| [Last](array/Last.md) | Returns the last element, or null if empty | No | No |
| [Length](array/Length.md) | Returns the length of a string, array, or object | No | No |
| [Prepend](array/Prepend.md) | Adds an element to the beginning of an array | No, returns new array | Yes |
| [RandomChoice](array/RandomChoice.md) | Selects a random element from an array | No | No |
| [Range](array/Range.md) | Generates an array of numbers in a range with optional step | No, returns new array | Yes |
| [RemoveAt](array/RemoveAt.md) | Removes the element at a specified index | No, returns new array | Yes |
| [RemoveFirst](array/RemoveFirst.md) | Removes the first element from an array | No, returns new array | Yes |
| [RemoveLast](array/RemoveLast.md) | Removes the last element from an array | No, returns new array | Yes |
| [Reverse](array/Reverse.md) | Reverses the order of elements | No, returns new array | Yes |
| [SelectMany](array/SelectMany.md) | Flattens a nested array field from each object | No, returns new array | Yes |
| [Skip](array/Skip.md) | Skips the first N elements | No, returns new array | Yes |
| [Slice](array/Slice.md) | Extracts a portion of an array by index range | No, returns new array | Yes |
| [Sort](array/Sort.md) | Sorts elements by type group then value | No, returns new array | Yes |
| [SortByField](array/SortByField.md) | Sorts objects by a field in ascending or descending order | No, returns new array | Yes |

## DateTime Functions

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [DateAdd](datetime/DateAdd.md) | Adds a time amount to a date | No, returns new string | No |
| [DateDiff](datetime/DateDiff.md) | Calculates the difference between two dates in a given unit | No | No |
| [DatePart](datetime/DatePart.md) | Extracts a component from a date (year, month, day, etc.) | No | No |
| [FormatDate](datetime/FormatDate.md) | Formats a date using a .NET format pattern | No, returns new string | No |
| [Now](datetime/Now.md) | Returns the current UTC date and time in ISO 8601 | No | No |
| [ParseDate](datetime/ParseDate.md) | Parses a date string and normalizes to ISO 8601 | No, returns new string | No |
| [Today](datetime/Today.md) | Returns the current UTC date without time | No | No |

## Lambda Functions

Higher-order functions that accept inline lambda expressions as arguments.

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [All](lambda/All.md) | Tests whether all elements satisfy a predicate | No | No |
| [Find](lambda/Find.md) | Returns the first element matching a predicate | No | No |
| [Each](lambda/Each.md) | Executes a lambda for each element (side effects only) | No, returns null | No |
| [Map](lambda/Map.md) | Transforms each element using a lambda | No, returns new array | Yes |
| [Reduce](lambda/Reduce.md) | Accumulates an array into a single value | No | No |
| [Any](lambda/Any.md) | Tests whether any element satisfies a predicate | No | No |
| [SortBy](lambda/SortBy.md) | Sorts an array using a lambda key extractor | No, returns new array | Yes |
| [Where](lambda/Where.md) | Filters an array using a lambda predicate | No, returns new array | Yes |

## Math Functions

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [Absolute](math/Absolute.md) | Returns the absolute value of a number | No | No |
| [Average](math/Average.md) | Returns the arithmetic mean of numeric values | No | No |
| [Ceiling](math/Ceiling.md) | Rounds up to the nearest integer | No | No |
| [Clamp](math/Clamp.md) | Constrains a number to a min/max range | No | No |
| [Floor](math/Floor.md) | Rounds down to the nearest integer | No | No |
| [Log](math/Log.md) | Returns the logarithm with an optional base | No | No |
| [Max](math/Max.md) | Returns the largest numeric value in an array | No | No |
| [Median](math/Median.md) | Returns the median value of a numeric array | No | No |
| [Min](math/Min.md) | Returns the smallest numeric value in an array | No | No |
| [Mode](math/Mode.md) | Returns the most frequently occurring value | No | No |
| [Power](math/Power.md) | Raises a number to a specified power | No | No |
| [RandomInt](math/RandomInt.md) | Generates a cryptographically secure random integer | No | No |
| [SquareRoot](math/SquareRoot.md) | Returns the square root of a number | No | No |
| [Sum](math/Sum.md) | Returns the sum of all numeric values in an array | No | No |

## Query Functions

Field-based query functions for filtering and projecting arrays of objects.

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [AllByField](query/AllByField.md) | Tests whether all objects match a field condition | No | No |
| [AnyByField](query/AnyByField.md) | Tests whether any object matches a field condition | No | No |
| [CountIf](query/CountIf.md) | Counts objects matching a field condition | No | No |
| [WhereByField](query/WhereByField.md) | Returns objects matching a field condition | No, returns new array | Yes |
| [FindByField](query/FindByField.md) | Returns the first object matching a field condition | No | No |
| [Omit](query/Omit.md) | Removes specified fields from each object | No, returns new array | Yes |
| [Project](query/Project.md) | Extracts specified fields from each object | No, returns new array | Yes |
| [Select](query/Select.md) | Extracts a single field value from each object | No, returns new array | Yes |

## Schema Functions

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [ValidateRequired](schema/ValidateRequired.md) | Checks that an object contains required non-null fields | No | No |
| [ValidateSchema](schema/ValidateSchema.md) | Validates data against a JSON Schema, returning errors | No, returns new array | Yes |

## String Functions

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [Contains](string/Contains.md) | Tests whether a string contains a substring (or array contains element) | No | No |
| [EndsWith](string/EndsWith.md) | Tests whether a string ends with a suffix | No | No |
| [Join](string/Join.md) | Joins array elements into a string with a separator | No, returns new string | Yes (string) |
| [ToLower](string/ToLower.md) | Converts a string to lowercase | No, returns new string | Yes (string) |
| [PadLeft](string/PadLeft.md) | Pads a string on the left to a specified width | No, returns new string | Yes (string) |
| [PadRight](string/PadRight.md) | Pads a string on the right to a specified width | No, returns new string | Yes (string) |
| [RandomString](string/RandomString.md) | Generates a cryptographically secure random string | No, returns new string | Yes (string) |
| [RegexMatch](string/RegexMatch.md) | Returns the first regex match | No | No |
| [RegexMatchAll](string/RegexMatchAll.md) | Returns all regex matches as an array | No, returns new array | Yes |
| [RegexMatchDetail](string/RegexMatchDetail.md) | Returns a detailed match object with capture groups | No, returns new object | Yes (object) |
| [RegexTest](string/RegexTest.md) | Tests whether a string matches a regex pattern | No | No |
| [Replace](string/Replace.md) | Replaces all occurrences of a substring | No, returns new string | Yes (string) |
| [Split](string/Split.md) | Splits a string into an array using a delimiter | No, returns new array | Yes (array) |
| [StartsWith](string/StartsWith.md) | Tests whether a string begins with a prefix | No | No |
| [Substring](string/Substring.md) | Extracts a portion of a string by position and optional length | No, returns new string | Yes (string) |
| [ToNumber](string/ToNumber.md) | Converts a string to a number (returns null on failure) | No | No |
| [ToUpper](string/ToUpper.md) | Converts a string to uppercase | No, returns new string | Yes (string) |
| [Trim](string/Trim.md) | Removes leading and trailing whitespace | No, returns new string | Yes (string) |

## Utility Functions

| Function | Description | Mutates | Chainable |
|----------|-------------|---------|-----------|
| [Base64Decode](utility/Base64Decode.md) | Decodes a Base64 string to UTF-8 | No, returns new string | Yes (string) |
| [Base64Encode](utility/Base64Encode.md) | Encodes a string to Base64 | No, returns new string | Yes (string) |
| [Clone](utility/Clone.md) | Creates a deep copy of a value | No, returns new value | Yes |
| [Coalesce](utility/Coalesce.md) | Returns the first non-null value from an array | No | Depends on result type |
| [Diff](utility/Diff.md) | Compares two objects and returns added, removed, and changed properties | No, returns new object | Yes (object) |
| [Equal](utility/Equal.md) | Tests deep equality of two values | No | No |
| [FromJson](utility/FromJson.md) | Parses a JSON string into a Jyro value | No, returns new value | Depends on result type |
| [HasProperty](utility/HasProperty.md) | Tests whether an object has a property (even if null) | No | No |
| [Keys](utility/Keys.md) | Returns all property names as an array | No, returns new array | Yes (array) |
| [Merge](utility/Merge.md) | Merges multiple objects into a new object | No, returns new object | Yes (object) |
| [NewGuid](utility/NewGuid.md) | Generates a new UUID v4 string | No | No |
| [Sleep](utility/Sleep.md) | Pauses execution for a specified number of milliseconds | No, returns null | No |
| [NotEqual](utility/NotEqual.md) | Tests deep inequality of two values | No | No |
| [ToBoolean](utility/ToBoolean.md) | Converts a value to boolean using Jyro truthiness rules | No | No |
| [ToJson](utility/ToJson.md) | Converts a Jyro value to a JSON string | No, returns new string | Yes (string) |
| [ToString](utility/ToString.md) | Converts a value to its string representation | No, returns new string | Yes (string) |
| [TypeOf](utility/TypeOf.md) | Returns the type name as a string | No | No |
| [Values](utility/Values.md) | Returns all property values as an array | No, returns new array | Yes (array) |
