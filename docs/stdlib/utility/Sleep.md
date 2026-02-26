# Sleep

Pauses script execution for the specified number of milliseconds.

## Signature

```
Sleep(ms)
```

## Parameters

- **ms** (number): Non-negative integer milliseconds to pause.

## Returns

- **null**: Always returns null.

## Description

Suspends execution for the given duration. The wall clock execution limit remains active during the sleep -- if the limit is reached while sleeping, the script is terminated with a `CancelledByHost` error.

In WASM environments, Sleep validates the argument but returns immediately (no-op) to avoid deadlocking the single-threaded runtime.

Negative or non-integer values raise a runtime error.

## Examples

```jyro
Sleep(100)
# Pauses for 100 milliseconds

Sleep(0)
# No-op, returns immediately

var delay = 500
Sleep(delay)
# Pauses for half a second
```
