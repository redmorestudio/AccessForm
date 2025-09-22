=========================================
AccessForm - PDF Accessibility Tool
Windows Installation & Usage Instructions
=========================================

WHAT THIS DOES:
---------------
AccessForm automatically converts Word documents and PDFs into fully accessible PDFs that comply with:
- WCAG 2.1 AA standards
- Section 508 requirements
- Government accessibility standards

The tool detects form fields, adds proper tags, and ensures your documents are screen-reader friendly.

SYSTEM REQUIREMENTS:
--------------------
- Windows 10 or Windows 11 (64-bit)
- 4GB RAM minimum (8GB recommended)
- 500MB free disk space
- Internet connection (for AI features)

INSTALLATION:
-------------
1. Extract this ZIP file to a folder on your computer
   Recommended: C:\AccessForm

2. No additional installation needed! The application is self-contained.

HOW TO RUN:
-----------
1. Open the folder where you extracted the files

2. Double-click on "AccessFormServer.exe"
   
3. A command window will open showing the server is running
   (Keep this window open while using the application)

4. Open your web browser (Chrome, Edge, or Firefox recommended)

5. Go to: http://localhost:5008

6. You should see the AccessForm interface!

USING THE APPLICATION:
----------------------
1. Drag and drop your Word (.docx) or PDF file onto the upload area
   OR click "Select Files" to browse

2. The application will automatically:
   - Convert Word files to PDF
   - Detect form fields
   - Add accessibility tags
   - Create both original and accessible versions

3. Download your files:
   - Original PDF (for reference)
   - Accessible PDF (fully compliant)
   - Accessibility Report (details of changes)

AI FEATURES:
------------
The application includes AI-powered field detection that's already configured.
No API keys or additional setup required - everything is included!

The AI helps with:
- Smart field detection
- Automatic labeling
- Contextual descriptions
- Form structure analysis

TROUBLESHOOTING:
----------------
If the application doesn't start:
1. Make sure no other application is using port 5008
2. Try running as Administrator (right-click AccessFormServer.exe > Run as administrator)
3. Check Windows Defender/Antivirus isn't blocking the application

If you can't access http://localhost:5008:
1. Make sure the command window is still open
2. Try http://127.0.0.1:5008 instead
3. Check your firewall settings

TO STOP THE APPLICATION:
-------------------------
Simply close the command window or press Ctrl+C in the command window

SUPPORT:
--------
For issues or questions, save the accessibility report and any error messages.

PRIVACY NOTE:
-------------
All processing happens locally on your computer. Your documents are never stored
or transmitted anywhere except for AI field detection (which uses secure APIs).

=========================================
Version 1.0 - Includes all AI features pre-configured
=========================================