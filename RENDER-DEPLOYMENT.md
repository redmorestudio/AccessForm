# Render Deployment Guide

## Quick Setup

1. **Push to GitHub** (already done):
   ```bash
   git push origin master
   ```

2. **Connect to Render**:
   - Go to https://render.com
   - Sign in with GitHub
   - Click "New +" → "Web Service"
   - Connect your GitHub repo: `redmorestudio/AccessForm`
   - Select the `WordToPdfConverter` directory

3. **Configure Service**:
   - **Name**: `accessform` (or whatever you prefer)
   - **Runtime**: Detected automatically as .NET
   - **Build Command**: `./build.sh`
   - **Start Command**: `dotnet run --configuration Release --urls http://0.0.0.0:$PORT`
   - **Plan**: Start with Free tier, upgrade to Starter ($7/month) if needed

4. **Environment Variables** (Render should auto-detect these from render.yaml):
   - `ASPNETCORE_ENVIRONMENT`: `Production`
   - `ASPNETCORE_URLS`: `http://0.0.0.0:$PORT`

## Features Configured

- ✅ **15-minute request timeout** for AI processing (vs Heroku's 30 seconds)
- ✅ **Python dependencies** installed (PyPDF, PyMuPDF)
- ✅ **Automatic GitHub deploys** on push
- ✅ **Production configuration** with proper port binding
- ✅ **File upload support** with persistent disk storage
- ✅ **Health checks** enabled

## Testing

Once deployed, your app will be available at:
`https://accessform.onrender.com` (or whatever name you choose)

The 1-5 minute AI processing should work without timeouts!

## Troubleshooting

- **Build fails**: Check build logs in Render dashboard
- **App won't start**: Verify environment variables are set
- **Still timing out**: May need to upgrade to paid plan for longer timeouts
- **Python errors**: Check that requirements.txt includes all needed packages

## Local Development

Continue using ngrok for instant testing:
```bash
ngrok http 5001
```

The Render deployment will stay stable while you iterate locally!
