# AccessForm Development Workflow

## Daily Development Workflow

### Making Changes
1. **Edit your code** in `/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/`
2. **Test locally**: 
   ```bash
   dotnet run --project AccessFormServer.csproj --urls "http://localhost:5008"
   ```
3. **Quick push** (for regular updates):
   ```bash
   ./push.sh
   ```
   This saves your work to GitHub without creating a release.

## Release Workflow (When You Have Something Significant)

### When to Create a Release
Create a release when you have:
- Fixed a major bug
- Added a new feature
- Improved performance significantly
- Made important UI changes
- Accumulated enough small changes worth bundling

### How to Create a Release

1. **Run the release script**:
   ```bash
   ./release.sh
   ```

2. **Follow the prompts**:
   - Enter new version number (e.g., 1.0.1, 1.1.0, 2.0.0)
   - Describe what's new
   - Optionally build packages

3. **The script automatically**:
   - Updates VERSION file
   - Updates CHANGELOG.md
   - Commits and pushes to GitHub
   - Creates a version tag
   - Optionally builds distribution package

4. **Complete on GitHub**:
   - Go to the releases page (link provided by script)
   - Add any additional notes
   - Upload built packages if created
   - Publish the release

## Version Numbering Guidelines

Use Semantic Versioning (MAJOR.MINOR.PATCH):

- **PATCH** (1.0.0 → 1.0.1): Bug fixes, minor improvements
- **MINOR** (1.0.0 → 1.1.0): New features, backward compatible
- **MAJOR** (1.0.0 → 2.0.0): Breaking changes, major overhaul

## Quick Commands Reference

### Daily Work
```bash
# Test your changes
dotnet run --project AccessFormServer.csproj --urls "http://localhost:5008"

# Save work to GitHub (no release)
./push.sh
```

### Release Work
```bash
# Create a new release
./release.sh

# Check current version
cat VERSION

# View recent changes
tail -20 CHANGELOG.md
```

### Building Without Releasing
```bash
# Just build locally for testing
dotnet build

# Create a local package without releasing
dotnet publish -c Release --self-contained -o ./local-build
```

## What Happens During a Release

1. **Version Update**: VERSION file is updated
2. **Changelog**: CHANGELOG.md gets new entry
3. **Git Tag**: Version tag created (e.g., v1.0.1)
4. **GitHub Push**: All changes pushed
5. **Package Build** (optional): Self-contained executable created
6. **GitHub Release**: Manual step to publish on GitHub

## Distribution to Users

### After Creating a Release

Users can get your software in these ways:

1. **Developers**: 
   ```bash
   git clone https://github.com/redmorestudio/AccessForm.git
   git checkout v1.0.1  # Specific version
   ```

2. **End Users**: Download from GitHub Releases page

3. **Docker Users** (if you set up Docker Hub):
   ```bash
   docker pull redmorestudio/accessform:1.0.1
   ```

## Example Release Scenarios

### Scenario 1: Bug Fix Release
```
Current: v1.0.0
New: v1.0.1
Notes: "Fixed form field detection for narrow table cells"
```

### Scenario 2: Feature Release
```
Current: v1.0.1
New: v1.1.0
Notes: "Added batch processing support for multiple files"
```

### Scenario 3: Major Update
```
Current: v1.1.0
New: v2.0.0
Notes: "Complete UI redesign with new accessibility features"
```

## Tips

- **Commit Often**: Use `./push.sh` regularly to save work
- **Release Thoughtfully**: Bundle related changes into releases
- **Document Changes**: Write clear release notes
- **Test Before Release**: Always test locally first
- **Version Consistently**: Follow semantic versioning

## Troubleshooting

### Push Fails
```bash
# Check status
git status

# Pull latest changes
git pull origin main

# Resolve conflicts if any, then push again
./push.sh
```

### Build Fails
```bash
# Clean build
dotnet clean
dotnet build

# Check for package updates
dotnet restore
```

### Release Script Issues
```bash
# Make script executable
chmod +x release.sh push.sh

# Run with bash explicitly
bash release.sh
```

---

**Remember**: 
- Use `./push.sh` for daily work
- Use `./release.sh` for significant updates
- Version numbers should reflect the magnitude of changes
