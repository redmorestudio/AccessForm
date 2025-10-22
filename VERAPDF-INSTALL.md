# veraPDF Installation Guide

## Quick Setup (Manual - 5 minutes)

Since automated installation proved difficult, here's the fastest manual approach:

### Option 1: GUI Installer (Recommended for Testing)

1. **Download veraPDF installer:**
   ```bash
   curl -L "https://software.verapdf.org/rel/1.26/verapdf-greenfield-1.26.2-installer.zip" -o ~/Downloads/verapdf-installer.zip
   cd ~/Downloads
   unzip verapdf-installer.zip
   cd verapdf-greenfield-1.26.2
   ```

2. **Run the GUI installer:**
   ```bash
   java -jar verapdf-izpack-installer-1.26.2.jar
   ```

   - Click through the installer
   - Install to: `/usr/local/verapdf`
   - Select "Mac and *nix Scripts" pack
   - Select "veraPDF Validation model" pack

3. **Verify installation:**
   ```bash
   /usr/local/verapdf/verapdf --version
   ```

### Option 2: Homebrew (If Available)

```bash
brew install verapdf
verapdf --version
```

### Option 3: Use Our Downloaded Java

We already have Java 21 downloaded in TestAssets. To use the manual installer:

```bash
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/verapdf-greenfield-1.26.2"

"/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/jdk-21.0.8.jdk/Contents/Home/bin/java" -jar verapdf-izpack-installer-1.26.2.jar
```

Then install to:
```
/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/verapdf
```

### Configuring VeraPdfService

Once installed, update `appsettings.json`:

```json
{
  "VeraPdf": {
    "ExecutablePath": "/usr/local/verapdf/verapdf",
    "JavaHome": "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/jdk-21.0.8.jdk/Contents/Home",
    "WorkingDirectory": "/tmp/verapdf-work"
  }
}
```

## Test veraPDF

```bash
# Test with a corpus file
verapdf --flavour ua1 --format json "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/veraPDF-corpus/PDF_UA-1/7.21 Fonts/7.21.6 Character encodings/7.21.6-t03-pass-a.pdf"
```

## Assets Already Downloaded

We have:
- ✅ Java JDK 21 in `TestAssets/jdk-21.0.8.jdk/`
- ✅ veraPDF installer in `TestAssets/verapdf-greenfield-1.26.2/`
- ✅ veraPDF corpus in `TestAssets/veraPDF-corpus/`

Just need to run the installer GUI manually (5 minutes).
