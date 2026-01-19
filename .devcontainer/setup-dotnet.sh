#!/bin/bash
set -e

echo "🔧 Setting up .NET environment for Challenge 2 and 4..."

# Create a temporary project to restore packages
TEMP_DIR="/tmp/dotnet-setup"
mkdir -p $TEMP_DIR
cd $TEMP_DIR

echo "📦 Creating temporary .NET project to cache NuGet packages..."
dotnet new console -n TempSetup --force

# Change into the project directory (dotnet new creates a subdirectory)
cd TempSetup

echo "📥 Installing required NuGet packages..."

# Azure SDKs (using pinned versions from workshop instructions)
dotnet add package Microsoft.Azure.Cosmos --version 3.56.0
dotnet add package Azure.AI.Projects --version 1.2.0-beta.5
dotnet add package Azure.Identity --version 1.17.1

# Microsoft.Extensions.AI
dotnet add package Microsoft.Extensions.AI --version 10.2.0
dotnet add package Microsoft.Extensions.AI.Abstractions --version 10.2.0

# Microsoft Agents
dotnet add package Microsoft.Agents.AI --version 1.0.0-preview.260108.1
dotnet add package Microsoft.Agents.AI.AzureAI --version 1.0.0-preview.260108.1

# Dependency Injection & Logging
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.2
dotnet add package Microsoft.Extensions.Logging --version 10.0.2
dotnet add package Microsoft.Extensions.Logging.Console --version 10.0.2

# JSON handling
dotnet add package Newtonsoft.Json --version 13.0.4

echo "🔄 Restoring packages to cache..."
dotnet restore

echo "🧹 Cleaning up temporary project..."
cd /
rm -rf $TEMP_DIR

echo "✅ .NET environment setup complete!"
echo "📌 .NET SDK $(dotnet --version) is ready"
echo "📦 All required NuGet packages are cached and ready to use"
