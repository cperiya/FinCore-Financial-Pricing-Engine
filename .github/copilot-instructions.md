# Copilot instructions

- Follow the existing modular-monolith boundaries and project-reference
  direction documented in [the architecture guide](../docs/architecture.md).
- Document every public C# class and method with XML comments. Explain
  purpose, parameters, return values, applicable exceptions, and important
  business rules. Do not invent exceptions or business rules that the code
  does not enforce.
- Update the README and directly related documentation when behavior or
  architecture changes. Document only functionality that is implemented;
  distinguish plans from completed work.
- Add an entry to [the learning journal](../docs/learning-journal.md) for each
  completed milestone, including key engineering decisions and what remains
  unimplemented.
- Use synthetic financial data only. Do not add SQL Server, EF Core, Redis,
  Docker, Azure, or CI/CD ahead of the milestone that requests them.
- Preserve existing files and unrelated changes. Add focused tests for new
  behavior and run the relevant build and test commands.
