# Coding standards

- Put `if` and `else` bodies on their own indented lines, even when the body is a single statement. Apply the same layout to other embedded control-flow statements.
- Explicitly type variables, including loop variables and `out` declarations. Use `var` only when the actual type cannot be named, such as an anonymous type produced by a LINQ projection; LINQ alone is not a reason to use `var`.
- Prefer `public` over `internal` accessibility, including top-level types that would otherwise default to internal. Keep private implementation details private and preserve necessary restrictive accessors.
- Do not seal classes by default. Decide which classes intentionally prohibit inheritance when wrapping up the framework.
- Use purposefully named classes or structs instead of tuples, with descriptive member names. Use immutable value types with value equality for composite dictionary keys.
- Follow the remaining naming and formatting conventions in `.editorconfig`.
