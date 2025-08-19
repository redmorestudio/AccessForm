#!/bin/bash

# AccessForm Quick Push Script
# For regular development updates (not releases)

echo "📝 AccessForm Quick Push"
echo "========================"
echo ""

# Check for uncommitted changes
if [[ -n $(git status -s) ]]; then
    echo "📋 Uncommitted changes found:"
    git status -s
    echo ""
    
    # Ask for commit message
    echo "Enter commit message (or 'skip' to cancel):"
    read COMMIT_MSG
    
    if [ "$COMMIT_MSG" = "skip" ]; then
        echo "❌ Push cancelled"
        exit 0
    fi
    
    # Add and commit
    git add .
    git commit -m "$COMMIT_MSG"
    echo "✅ Changes committed"
else
    echo "✅ No uncommitted changes"
fi

# Push to GitHub
echo ""
echo "📤 Pushing to GitHub..."
git push origin master:main

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Successfully pushed to GitHub!"
    echo "🔗 View at: https://github.com/redmorestudio/AccessForm"
else
    echo "❌ Push failed. Please check your connection and try again."
fi
