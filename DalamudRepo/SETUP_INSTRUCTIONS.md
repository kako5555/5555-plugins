# Private RetainerPriceDrop Repository Setup

## For Repository Owner (You):

### 1. Create Private GitHub Repository
1. Go to GitHub and create a **PRIVATE** repository (e.g., `RetainerPriceDrop-Private`)
2. Make sure it's set to **Private** so only invited people can access it

### 2. Upload Files
1. Upload the entire `DalamudRepo` folder contents to your repository:
   - `repo.json` (edit the URLs first - see below)
   - `icon.png`
   - The plugin zip file (as a Release)

### 3. Edit repo.json
Replace `YOUR_GITHUB_USERNAME` and `YOUR_PRIVATE_REPO` with your actual values in:
- DownloadLinkInstall
- DownloadLinkUpdate
- DownloadLinkTesting
- IconUrl

### 4. Create a Release
1. Go to Releases → Create New Release
2. Tag: `v1.1.3.0`
3. Upload `RetainerPriceDrop_v1.1.3.0.zip` as `RetainerPriceDrop.zip`
4. Publish the release

### 5. Invite Friends
1. Go to Settings → Manage Access
2. Click "Add people"
3. Add your friends' GitHub usernames
4. They need to accept the invitation

---

## For Your Friends:

### 1. Get a Personal Access Token
1. Go to GitHub → Settings → Developer Settings → Personal Access Tokens → Tokens (classic)
2. Generate new token with `repo` scope (to access private repos)
3. Copy the token (you won't see it again!)

### 2. Add Repository to Dalamud
1. In FFXIV, type `/xlsettings`
2. Go to "Experimental" tab
3. Under "Custom Plugin Repositories", add:
   ```
   https://raw.githubusercontent.com/YOUR_USERNAME/YOUR_REPO/main/repo.json
   ```
4. You may need to add authentication:
   - Some Dalamud versions support adding tokens directly
   - Or use format: `https://YOUR_TOKEN@raw.githubusercontent.com/...`

### 3. Install Plugin
1. Type `/xlplugins`
2. Click "Available Plugins"
3. Search for "Retainer Price Drop"
4. Install it!

---

## Important Security Notes:

⚠️ **NEVER share your personal access token publicly**
⚠️ **Keep the repository PRIVATE**
⚠️ **Only invite people you trust**
⚠️ **Tokens should have minimal permissions (just `repo` scope)**

## Alternative: Direct Installation
If the repo method doesn't work, friends can:
1. Download the zip file directly from the GitHub releases
2. Extract to `%AppData%\XIVLauncher\installedPlugins\RetainerPriceDrop\`
3. Restart the game or reload plugins