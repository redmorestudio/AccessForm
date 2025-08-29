---
title: Technical Requirements - Browser-Only Architecture
type: note
permalink: product-requirements/technical-requirements-browser-only-architecture-1
tags:
- '["technical-requirements"'
- '"browser-based"'
- '"local-processing"'
- '"architecture"'
- '"prd"]'
---

# Technical Requirements and Constraints

# Technical Requirements and Constraints

## Core Architecture Decision
**Browser-based application with AI-enhanced processing**

## Key Technical Constraints

### Browser-First Approach
- Primary processing interface in the browser
- PDF manipulation happens client-side
- No installation required - runs directly in modern web browsers
- Accessible via URL (can be hosted on static hosting or run locally)
- Integration with external AI services for intelligent processing

### Processing Requirements
- File processing occurs in the user's browser
- AI services handle intelligent field detection and optimization
- Leverage browser APIs for file manipulation
- **Strict rule: No client/citizen data storage** - only form templates and structures

### AI Service Integration
- External AI APIs for field detection and form understanding
- Secure API key management
- Only form structure and metadata sent to AI - no PII
- Fallback processing for when AI services unavailable

## Technical Advantages

### Security & Compliance
- No client data ever stored - only templates and form structures
- AI services receive only structural metadata, no PII
- Simplifies government compliance requirements
- Clear data handling policy for government approval

### Deployment & Maintenance
- Simple static site hosting (GitHub Pages, S3, etc.)
- Minimal server infrastructure (only for AI API proxy if needed)
- Browser handles processing load
- Offline capability for core features once loaded
- Predictable AI API costs based on usage

### User Benefits
- No installation required (critical for Janet)
- Works on any modern browser
- Bookmark-able like any website
- Familiar web interface
- No IT department approval needed for software installation
- AI-enhanced accuracy and speed

## Browser Technologies to Leverage

### File Handling
- File API for reading Word/PDF documents
- Drag and drop API for batch file upload
- File System Access API (where supported) for better file management

### Processing & Storage
- Web Workers for parallel processing of multiple files
- IndexedDB for templates and application data (no client data)
- Local Storage for user preferences and settings
- Persistent storage OK for app data, templates, and user settings
- WebAssembly if heavy processing needed (PDF manipulation libraries)

### AI Integration
- Secure API client implementation
- Request queuing and rate limiting
- Response caching for common patterns
- Error handling and fallback logic

### PDF Manipulation
- PDF.js for reading and rendering PDFs
- pdf-lib or similar for PDF creation/modification
- Consider jsPDF for PDF generation from scratch

### Word Document Processing
- mammoth.js or similar for reading .docx files
- Extract form fields, styles, and structure

## Performance Considerations
- Batch processing must handle multiple files without freezing browser
- Progress indicators for long-running operations
- Chunked processing for large files
- Memory management for processing multiple documents

## Browser Compatibility Target
- Chrome/Edge 90+
- Firefox 90+
- Safari 14+
- No Internet Explorer support

## Progressive Enhancement Strategy
- Core features work on all target browsers
- Advanced features (like File System Access API) enhance experience where available
- Graceful fallbacks for unsupported APIs

## Development Implications
- Pure JavaScript/TypeScript application
- No backend API development initially
- Focus on client-side libraries and tools
- Static build process (Webpack/Vite/etc.)
- Can be developed and tested entirely locally


[FULL TECHNICAL SPECIFICATION CONTINUES...]