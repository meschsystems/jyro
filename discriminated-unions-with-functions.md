# Discriminated Unions with Functions

## With functions, the full picture

### Union declaration generates constructor functions

```jyro
union Shape
    Circle(radius: number)
    Rect(width: number, height: number)
    Triangle(base: number, height: number)
end
```

This auto-generates three constructor functions - they're just functions, same namespace, same call syntax, same linker resolution:

```jyro
var s1 = Circle(5)           # returns {_variant: "Circle", radius: 5}
var s2 = Rect(10, 20)        # returns {_variant: "Rect", width: 10, height: 20}
```

The constructors validate argument types (the type hints in the union declaration are enforced), so you can't create a malformed variant. And since they're functions - no Data access, pure, just args in, tagged object out.

### Functions become the natural home for `match`

```jyro
func Area(shape)
    match shape do
        case Circle(r) then
            return 3.14159 * Power(r, 2)
        case Rect(w, h) then
            return w * h
        case Triangle(b, h) then
            return 0.5 * b * h
    end
end

func Describe(shape)
    match shape do
        case Circle(r) then
            return "Circle with radius " + r
        case Rect(w, h) then
            return w + "x" + h + " rectangle"
        case Triangle(b, h) then
            return "Triangle " + b + " by " + h
    end
end
```

The compiler knows the closed set of variants from the `union` declaration, so:
- **Missing a case → compile error.** Forget `Triangle` and the compiler tells you.
- **Destructuring binds fields to local variables.** `Circle(r)` binds `r` to `radius` - scoped to that case block.
- **No `default` needed.** If all variants are covered, you're done. Add `default` only for forward-compatibility if the union might grow.

### Composability with stdlib - same as any function

```jyro
union Shape
    Circle(radius: number)
    Rect(width: number, height: number)
end

func Area(shape)
    match shape do
        case Circle(r) then
            return 3.14159 * Power(r, 2)
        case Rect(w, h) then
            return w * h
    end
end

func IsLarge(shape)
    return Area(shape) > 100
end

# Shapes come in from the host as tagged objects - JSON-native
var areas = Map(Data.shapes, s => Area(s))
var large = Where(Data.shapes, s => IsLarge(s))
Data.totalArea = Sum(areas)
Data.largeShapes = large
```

Functions wrap match logic, lambdas bridge to higher-order stdlib. No dynamic dispatch, no function references - just the same patterns.

### The practical example - API results

```jyro
union ApiResult
    Success(data)
    NotFound(message: string)
    ValidationError(field: string, reason: string)
end

func StatusCode(result)
    match result do
        case Success(d) then
            return 200
        case NotFound(msg) then
            return 404
        case ValidationError(f, r) then
            return 422
    end
end

func ErrorMessage(result)
    match result do
        case Success(d) then
            return null
        case NotFound(msg) then
            return msg
        case ValidationError(f, r) then
            return "Field '" + f + "': " + r
    end
end

Data.statusCode = StatusCode(Data.apiResult)
Data.error = ErrorMessage(Data.apiResult)
exit
```

### What the host sees (JSON)

The tagged objects are plain JSON - no special runtime types:

```json
{
  "apiResult": {"_variant": "Success", "data": {"id": 42, "name": "Alice"}},
  "shapes": [
    {"_variant": "Circle", "radius": 5},
    {"_variant": "Rect", "width": 10, "height": 20}
  ]
}
```

Hosts can construct these in whatever language they use. The `_variant` field is the discriminator - chosen to be unlikely to collide with user data (underscore prefix).

### Design constraints unions would need

| Rule | Rationale |
|---|---|
| Variant names must be unique across all unions in the script | No ambiguity - `Circle(5)` resolves to exactly one union |
| Variant constructors can't shadow stdlib/host functions | Same rule as user functions - linker error |
| User functions can't shadow variant constructors | Same namespace - linker error |
| `match` without exhaustive coverage → compile error | The whole point of closed unions |
| `match` on a non-union value → use `switch` instead | `match` is specifically for union destructuring |
| Untyped parameters (no `: type` hint) → `AnyParam` | Variant fields can hold any value if untyped |

### What functions buy us that we didn't have before

Without functions, `match` at top level is a one-shot pattern match - useful but limited. With functions:

1. **Reusable match logic** - write `Area(shape)` once, call it everywhere
2. **Recursive unions** - a function can match a tree node and recurse on children
3. **Composition** - `StatusCode(result)` calls `ErrorMessage(result)` calls whatever
4. **Lambda bridge** - `Map(shapes, s => Area(s))` - unions flow through the stdlib pipeline

Functions are really the prerequisite that makes unions practical rather than just a fancy `switch`.
