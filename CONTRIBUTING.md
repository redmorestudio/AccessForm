# Contributing to AccessForm

Thank you for your interest in contributing to AccessForm!

## How to Contribute

### Reporting Issues

1. Check existing issues first
2. Use the issue template
3. Include:
   - OS and version
   - AccessForm version
   - Steps to reproduce
   - Expected vs actual behavior
   - Sample files (if applicable)

### Feature Requests

1. Check existing requests
2. Describe the use case
3. Explain the benefit
4. Provide examples

### Code Contributions

Note: As proprietary software, code contributions require a Contributor License Agreement (CLA).

1. Contact us first: contribute@redmorestudio.com
2. Sign the CLA
3. Fork the repository
4. Create a feature branch
5. Follow coding standards
6. Add tests
7. Submit a pull request

## Development Setup

```bash
# Clone your fork
git clone https://github.com/YOUR-USERNAME/AccessForm.git

# Add upstream remote
git remote add upstream https://github.com/redmorestudio/AccessForm.git

# Install dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Run locally
dotnet run --project AccessFormServer.csproj
```

## Coding Standards

- C# 12 / .NET 8.0
- Follow Microsoft C# conventions
- XML documentation for public APIs
- Unit tests for new features
- Accessibility-first design

## Testing

- Unit tests in `/Tests`
- Integration tests for PDF processing
- Manual accessibility testing
- Cross-platform testing

## Questions?

Email: contribute@redmorestudio.com