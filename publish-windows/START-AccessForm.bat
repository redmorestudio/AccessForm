@echo off
echo =========================================
echo Starting AccessForm PDF Accessibility Tool
echo =========================================
echo.
echo The application is starting...
echo.
echo Once you see "Now listening on: http://localhost:5008"
echo Open your web browser and go to: http://localhost:5008
echo.
echo Keep this window open while using the application.
echo Press Ctrl+C to stop the server when done.
echo =========================================
echo.
AccessFormServer.exe --urls "http://localhost:5008"
pause