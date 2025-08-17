# AIConsoleAppRecording

![AIConsoleAppRecording Introduction](Documents/Landing_Image_Introduction.png)

A powerful .NET 8 console application for audio recording, transcription, and intelligent note-taking. Record audio from your microphone, system audio, or both simultaneously, then automatically transcribe it using Azure OpenAI Whisper, generate summaries, and save to Notion.

## Screenshots

<details>
<summary>📸 Click to view application screenshots</summary>

### Running the Application
![Application Running](Documents/Example_Of_Running_Application_Until_Save_To_Notion.png)

### Result in Notion
![Notion Result](Documents/Example_Transcript_Save_To_Notion.png)

</details>

## Features

- 🎙️ **Flexible Audio Recording**
  - Record from microphone only
  - Record system audio only
  - Record both sources mixed together
  - Real-time volume level indicators
  - Support for multiple microphone devices
  - Automatic audio format conversion (MP3 compression)

- 🔤 **Automatic Transcription**
  - Powered by Azure OpenAI Whisper
  - Multi-language support (Auto-detect, English, Thai)
  - **Smart file splitting for large recordings** (20-minute segments)
  - **Automatic batch processing** for files over 25MB
  - High accuracy transcription

- 🎯 **Advanced Audio Processing**
  - **Process existing audio files** (WAV, MP3, M4A, MP4)
  - **Mix separate microphone and system audio files**
  - **Automatic MP3 conversion** for optimal file sizes
  - **Intelligent segmentation** for long recordings

- 📝 **AI-Powered Summarization** (Optional)
  - Generate concise summaries using Azure AI
  - Smart title generation for better organization
  - Language-aware processing

- 📚 **Notion Integration**
  - Automatically create pages in your Notion database
  - **Handles large transcripts** (splits into 2000-char blocks)
  - Structured content with title, date, summary, and full transcript
  - Seamless workflow integration

- 🛡️ **Reliability & Protection**
  - **Quota protection** - stops processing when rate limits are detected
  - **Partial transcript recovery** - saves completed segments
  - **Automatic retries** with smart delays
  - Local backup for all transcripts

- 💾 **File Management**
  - Save transcripts as timestamped text files
  - Includes both summary and full transcript
  - Automatic cleanup of temporary files
  - Preserves original recordings

## Prerequisites

- .NET 8.0 SDK or later
- Azure OpenAI subscription with Whisper deployment
- Notion account with an integration token
- (Optional) Azure AI subscription for summarization

## Installation

1. Clone the repository:
```bash
git clone https://github.com/T-T-Software-Solution/airecording.git
cd airecording
```

2. Restore NuGet packages:
```bash
dotnet restore
```

3. Build the application:
```bash
dotnet build
```

## Configuration

### Required: Set User Secrets

The application uses secure user secrets for sensitive configuration. Set up the following:

```bash
# Initialize user secrets (first time only)
dotnet user-secrets init

# Azure Whisper Configuration (REQUIRED)
dotnet user-secrets set "Azure:EndpointUrl" "https://your-resource.openai.azure.com/openai/deployments/whisper/audio/transcriptions?api-version=2024-06-01"
dotnet user-secrets set "Azure:ApiKey" "your-azure-api-key"

# Notion Configuration (REQUIRED)
dotnet user-secrets set "Notion:ApiToken" "your-notion-integration-token"
dotnet user-secrets set "Notion:DatabaseId" "your-notion-database-id"

# Azure AI Configuration (OPTIONAL - for summarization)
dotnet user-secrets set "AzureAI:EndpointUrl" "https://your-resource.openai.azure.com/openai/deployments/gpt-4/chat/completions?api-version=2024-06-01"
dotnet user-secrets set "AzureAI:ApiKey" "your-azure-ai-api-key"
```

### Setting up Notion

1. Create a Notion integration:
   - Go to https://www.notion.so/my-integrations
   - Click "New integration"
   - Give it a name and select your workspace
   - Copy the integration token

   ![Create Notion Integration](Documents/Create_Notion_Integration.png)

2. Create a database in Notion:
   - Create a new database with at least two properties:
     - **Name** (Title property) - for the transcript title
     - **Date** (Date property) - for the recording timestamp
   - Share the database with your integration
   
   ![Attach Connection to Database](Documents/Attach_Connection_To_Database_In_Notion.png)
   
   - Copy the database ID from the URL
   
   ![Where is Database ID](Documents/Where_Is_Database_Id_In_Notion.png)

### Optional: Environment Variables for Defaults

Skip the selection prompts by setting default values:

#### Windows

**Using Command Prompt (CMD):**
```cmd
setx RECORDING_MODE "both"
setx RECORDING_LANGUAGE "auto"
setx RECORDING_MICROPHONE "0"
```

**Using PowerShell:**
```powershell
[Environment]::SetEnvironmentVariable('RECORDING_MODE', 'both', 'User')
[Environment]::SetEnvironmentVariable('RECORDING_LANGUAGE', 'auto', 'User')
[Environment]::SetEnvironmentVariable('RECORDING_MICROPHONE', '0', 'User')
```

#### Linux/macOS

**For current session:**
```bash
export RECORDING_MODE="both"
export RECORDING_LANGUAGE="auto"
export RECORDING_MICROPHONE="0"
```

**Make permanent (add to ~/.bashrc or ~/.zshrc):**
```bash
echo 'export RECORDING_MODE="both"' >> ~/.bashrc
echo 'export RECORDING_LANGUAGE="auto"' >> ~/.bashrc
echo 'export RECORDING_MICROPHONE="0"' >> ~/.bashrc
```

#### Valid Values

- **RECORDING_MODE**: `mic`, `system`, `both`
- **RECORDING_LANGUAGE**: `auto`, `en`, `th`
- **RECORDING_MICROPHONE**: Device number (0, 1, 2, etc.)

## Usage

### Quick Start

```bash
dotnet run
```

You'll be presented with two main options:
1. **Record new audio** - Start a new recording session
2. **Process existing audio file(s)** - Process previously recorded files

### Recording New Audio

1. Select **operation mode**: Choose "1" for recording
2. Select **recording mode**: Microphone, system audio, or both
3. Select **language**: Auto-detect, English, or Thai
4. Select **microphone**: Choose from available devices (if applicable)
5. Press **Enter** to start recording
6. Press **Enter** again to stop

The application automatically:
- Converts large files to MP3 for compression
- Splits files over 25MB into 20-minute segments
- Transcribes each segment with Azure Whisper
- Combines transcripts into one document
- Generates AI summary (if configured)
- Creates Notion page with full content
- Saves local backup

### Processing Existing Files

#### Single File Processing
```bash
Select operation mode: 2
Select file processing mode: 1
Enter audio file path: C:\recordings\meeting.wav
```

#### Mixing Separate Audio Files
```bash
Select operation mode: 2
Select file processing mode: 2
Enter microphone WAV file path: C:\temp\recording.mic.wav
Enter system audio WAV file path: C:\temp\recording.system.wav
```

The application will:
- Mix the audio files (handling different sample rates/channels)
- Process as a single combined file
- Apply same splitting and transcription logic

### Advanced Features

#### Large File Handling
Files over 25MB are automatically:
- Split into 20-minute segments
- Each segment converted to MP3 (~18MB from 220MB WAV)
- Processed in batch with progress indicators
- Combined into single transcript

#### Quota Protection
- Detects Azure rate limits (429 errors)
- Stops processing to prevent charges
- Saves partial transcripts
- Shows clear error messages

### Example Output

![Example of Running Application](Documents/Example_Of_Running_Application_Until_Save_To_Notion.png)

## Output

### Notion Page
- **Title**: "Transcription - YYYY-MM-DD HH:MM" or AI-generated title
- **Date**: Recording timestamp
- **Content**: Summary (if available) and full transcript

![Example Transcript in Notion](Documents/Example_Transcript_Save_To_Notion.png)

### Local File
- **Filename**: `Transcript-YYYYMMDD-HHMMSS.txt`
- **Contents**:
  - Recording timestamp
  - Summary (if available)
  - Full transcript

## Troubleshooting

### Environment Variables Not Working

If the app still prompts for settings despite having environment variables set:

1. **Check if variables are persisted (Windows):**
```powershell
[Environment]::GetEnvironmentVariable('RECORDING_MODE', 'User')
[Environment]::GetEnvironmentVariable('RECORDING_LANGUAGE', 'User')
[Environment]::GetEnvironmentVariable('RECORDING_MICROPHONE', 'User')
```

2. **Restart your terminal** after setting variables with `setx`

3. **For current session only (Windows):**
```cmd
set RECORDING_MODE=both
set RECORDING_LANGUAGE=auto
set RECORDING_MICROPHONE=0
```

### Removing Environment Variables

**Windows (PowerShell):**
```powershell
[Environment]::SetEnvironmentVariable('RECORDING_MODE', $null, 'User')
[Environment]::SetEnvironmentVariable('RECORDING_LANGUAGE', $null, 'User')
[Environment]::SetEnvironmentVariable('RECORDING_MICROPHONE', $null, 'User')
```

**Linux/macOS:**
```bash
unset RECORDING_MODE
unset RECORDING_LANGUAGE
unset RECORDING_MICROPHONE
```

### Audio Recording Issues

- **No audio devices found**: Ensure your microphone is connected and recognized by Windows
- **System audio not recording**: Some systems require specific audio drivers or permissions
- **Low volume**: Check your system audio settings and microphone levels
- **Different audio formats**: The app automatically handles different sample rates and channel counts when mixing

### API Errors

- **Azure Whisper**: Verify your endpoint URL includes the full path with API version
- **Notion**: Ensure your integration has access to the database
- **Rate limits**: The app automatically adds 5-second delays between segments
- **File too large**: Files over 25MB are automatically split into segments
- **Invalid file format**: Supported formats are WAV, MP3, M4A, MP4

### Large File Issues

- **"File exceeds 25MB limit"**: This is handled automatically with splitting
- **"Quota exceeded"**: The app stops processing to prevent charges
- **"Notion 2000 char limit"**: Long texts are automatically split into multiple blocks
- **MP3 conversion fails**: The app falls back to WAV processing

## Project Structure

```
airecording/
├── Program.cs              # Main application entry point
├── AudioService.cs         # Audio recording logic using NAudio
├── AzureWhisperService.cs  # Azure OpenAI Whisper integration
├── AzureSummaryService.cs  # Azure AI summarization service
├── NotionService.cs        # Notion API integration
├── RecordingSettings.cs   # Settings and environment management
├── AIConsoleAppRecording.csproj  # Project file
└── README.md              # This file
```

## Dependencies

- **NAudio** - Audio recording and processing
- **Microsoft.Extensions.Configuration** - Configuration management
- **Microsoft.Extensions.Configuration.Json** - JSON configuration support
- **Microsoft.Extensions.Configuration.UserSecrets** - Secure secrets storage
- **System.Text.Json** - JSON serialization

## Security

- All API keys and tokens are stored securely using .NET user secrets
- Never commit secrets to version control
- Use environment variables for non-sensitive defaults only

## License

This project is provided as-is for educational and personal use.

## Contributing

Contributions are welcome! Please feel free to submit pull requests or open issues for bugs and feature requests.

## Support

For issues or questions, please open an issue on the GitHub repository.