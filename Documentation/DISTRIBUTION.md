# AccessForm Distribution Guide

## Overview
AccessForm is distributed in multiple ways to meet different organizational needs:

1. **Source Code** - For developers who want to customize
2. **Docker Container** - For easy deployment in organizations
3. **Standalone Executables** - For end users (coming soon)
4. **Cloud Hosting** - SaaS option (planned)

## Current Version: v1.0.0

## Distribution Methods

### 1. For Developers - Source Code

**Repository**: https://github.com/redmorestudio/AccessForm

```bash
# Clone the repository
git clone https://github.com/redmorestudio/AccessForm.git
cd AccessForm

# Build and run
dotnet build AccessFormServer.csproj
dotnet run --project AccessFormServer.csproj --urls "http://localhost:5008"
```

**Requirements**:
- .NET 9.0 SDK
- Git
- Visual Studio 2022 or VS Code (optional)

### 2. For Organizations - Docker

**Quick Deploy**:
```bash
# Using Docker Compose
docker-compose up -d

# Or using Docker directly
docker run -p 5008:80 redmorestudio/accessform:latest
```

**Custom Configuration**:
```yaml
version: '3.8'
services:
  accessform:
    image: redmorestudio/accessform:latest
    ports:
      - "5008:80"
    environment:
      - SYNCFUSION_LICENSE_KEY=YOUR_KEY_HERE
    volumes:
      - ./data:/app/data
```

### 3. For End Users - Standalone Executables

**Windows** (Coming Soon):
- Download `AccessForm-Windows-x64.zip`
- Extract to desired location
- Run `AccessForm.exe`
- Access at http://localhost:5008

**macOS** (Coming Soon):
- Download `AccessForm-macOS.dmg`
- Drag to Applications folder
- Run from Applications
- Access at http://localhost:5008

**Linux** (Coming Soon):
- Download `AccessForm-Linux-x64.tar.gz`
- Extract: `tar -xzf AccessForm-Linux-x64.tar.gz`
- Run: `./AccessForm`
- Access at http://localhost:5008

## Version Management

### Checking Your Version
- Web UI: Look at the footer
- API: GET `/api/version`
- Docker: `docker inspect redmorestudio/accessform:latest`

### Updating

**Source Code**:
```bash
git pull origin main
dotnet build
```

**Docker**:
```bash
docker pull redmorestudio/accessform:latest
docker-compose down
docker-compose up -d
```

## Release Channels

### Stable (Recommended)
- Tag: `latest` or `v1.x.x`
- Updated monthly
- Production-ready

### Beta
- Tag: `beta` or `v1.x.x-beta`
- Updated weekly
- Preview features

### Nightly
- Tag: `nightly`
- Updated daily
- Development builds

## Deployment Configurations

### Small Organization (1-50 users)
```yaml
services:
  accessform:
    image: redmorestudio/accessform:latest
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 2G
```

### Medium Organization (50-500 users)
```yaml
services:
  accessform:
    image: redmorestudio/accessform:latest
    deploy:
      replicas: 3
      resources:
        limits:
          cpus: '4'
          memory: 4G
```

### Large Organization (500+ users)
- Contact us for enterprise deployment options
- Kubernetes configurations available
- High-availability setup documentation

## Syncfusion License

AccessForm requires a Syncfusion license for production use:

1. **Get a License**:
   - Community (free): https://www.syncfusion.com/products/communitylicense
   - Commercial: https://www.syncfusion.com/sales/products

2. **Configure License**:
   
   **Environment Variable**:
   ```bash
   SYNCFUSION_LICENSE_KEY=YOUR_LICENSE_KEY
   ```
   
   **Docker**:
   ```yaml
   environment:
     - SYNCFUSION_LICENSE_KEY=YOUR_LICENSE_KEY
   ```
   
   **Source Code**:
   Edit `Program.cs` and replace the license key.

## Support

### Community Support
- GitHub Issues: https://github.com/redmorestudio/AccessForm/issues
- Discussions: https://github.com/redmorestudio/AccessForm/discussions

### Commercial Support
- Email: support@redmorestudio.com
- Priority support available with enterprise license

## Changelog

### v1.0.0 (Current)
- Initial release
- Word to PDF conversion
- PDF remediation
- WCAG 2.1 AA compliance
- Section 508 compliance
- Form field detection
- Dual output (normal + accessible)

## Roadmap

### v1.1.0 (Q1 2025)
- Batch processing
- API improvements
- Enhanced field detection

### v1.2.0 (Q2 2025)
- AI-powered field recognition
- Multi-language support
- Cloud storage integration

### v2.0.0 (Q3 2025)
- Full SaaS platform
- Team collaboration
- Template library

## Security

### Reporting Security Issues
- Email: security@redmorestudio.com
- Do not create public issues for security vulnerabilities

### Security Updates
- Critical: Released immediately
- High: Within 7 days
- Medium: Within 30 days
- Low: Next regular release

## License

MIT License - See LICENSE file for details

## Contributing

See CONTRIBUTING.md for guidelines on contributing to AccessForm.
