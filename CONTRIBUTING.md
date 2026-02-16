# Contributing to RestLib

First off, thanks for taking the time to contribute! 🎉

The following is a set of guidelines for contributing to RestLib. These are mostly guidelines, not rules. Use your best judgment, and feel free to propose changes to this document in a pull request.

## getting Started

### Prerequisites

*   [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.
*   An IDE of your choice (VS Code, Visual Studio, Rider).

### Building the Project

The project uses a standard solution file. You can build it from the root directory:

```bash
dotnet build
```

### Running Tests

We have comprehensive test coverage. Please ensure all tests pass before submitting a PR.

```bash
dotnet test
```

### Running Benchmarks

If you are making performance-sensitive changes, please run the benchmarks to ensure no regressions:

```bash
cd benchmarks/RestLib.Benchmarks
dotnet run -c Release
```

## How to Contribute

1.  **Fork the repository** on GitHub.
2.  **Clone your fork** locally.
3.  **Create a branch** for your feature or bugfix (`git checkout -b feature/amazing-feature`).
4.  **Make your changes**.
5.  **Run tests** to ensure no regressions.
6.  **Commit your changes** with a clear commit message.
    *   Use the present tense ("Add feature" not "Added feature").
    *   Reference issues if applicable.
7.  **Push to your fork** (`git push origin feature/amazing-feature`).
8.  **Open a Pull Request** against the `main` branch.

## Coding Standards

*   We use **file-scoped namespaces**.
*   We use **`var`** where the type is apparent.
*   We follow standard C# naming conventions (PascalCase for classes/methods, camelCase for parameters).
*   **Documentation**: Public APIs should be documented with XML comments.
*   **EditorConfig**: The project includes an `.editorconfig` file to help maintain consistent coding styles.

## Reporting Bugs

Bugs are tracked as GitHub issues. When filing an issue, please include:

*   A clear title and description.
*   Steps to reproduce the issue.
*   Expected behavior vs. actual behavior.
*   Any relevant logs or stack traces.

## License

By contributing, you agree that your contributions will be licensed under its MIT License.
