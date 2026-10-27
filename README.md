# Unpaused 90'

Unpaused 90' is an AI-powered football match companion for fans who missed part
of a live match. It combines a simulated match timeline with concise,
personalized catch-up stories, tactical explanations, and an audio recap so a
fan can return to the action without reading a full match report.

## What the app does

- **Live match dashboard** - Displays the current score, match minute, match
  status, stadium, possession, and other match statistics.
- **Catch me up** - Select the minute where you stopped watching and generate a
  recap of what happened during the missed window.
- **Personalized recaps** - Tailor the story for a casual fan, an
  analyst/coach, or a child. Recaps can be neutral or focused on the home or
  away team.
- **Multiple recap lengths** - Choose a quick one-minute sprint, a deeper
  five-minute recap, or a full-match story.
- **Key moments timeline** - Browse goals, cards, substitutions, and pressure
  sequences in chronological order.
- **Why it matters explanations** - Select a timeline event to request an
  AI-generated explanation of its tactical or match impact.
- **Dynamic match simulation** - Advance through a simulated match, reset it to
  a random starting minute, and view time-sliced score, events, insights, and
  possession data.
- **Audio co-host** - Convert a recap to spoken audio with Azure AI Speech.
  The player includes playback controls, progress, speed selection, transcript
  text, and alternating co-host personas.
- **English and Spanish** - Switch the dashboard and generated audio feed
  between English and Spanish.
- **Responsive broadcast-style UI** - The interface adapts from desktop to
  mobile and includes animated goal, yellow-card, red-card, and substitution
  moments.

## UI overview

The dashboard is organized as a football broadcast console:

1. **Navigation bar** - Branding, match context, and language controls.
2. **Live Grounds strip** - Current match, score, clock, simulation status,
   and the random simulation reset action.
3. **Catch-Up Console** - The primary control surface for the stopped minute,
   audience persona, team bias, recap length, language, and the **Catch Me Up**
   action.
4. **Score and match panel** - Team crests, scoreline, match clock, pitch
   presentation, and event animations.
5. **Timeline and key moments** - Match events with filters and selectable
   moments. Selecting an event opens its explanation.
6. **Insight and statistics cards** - “What happened,” “Why it matters,”
   momentum, what to watch next, possession, and match statistics.
7. **Final score/story view** - Full-time recap content, estimated listening
   duration, and summary controls.
8. **Audio player dock** - Persistent playback controls for generated recaps,
   including speaker identity, transcript, progress, and playback speed.

The visual language uses a light editorial sports layout with Premier League
purple and magenta accents, mint highlights, pitch-grid texture, broadcast
panels, responsive cards, and motion effects for important match events.

## Architecture

```text
react-unpaused-90/       React + TypeScript frontend
        │
        │ REST /api/v1
        ▼
NETUnpaused90/           ASP.NET Core minimal API backend
        ├── MatchService      Loads and simulates match data
        ├── AiService         Generates recaps and event explanations
        └── SpeechService     Generates MP3 audio recaps
```

The frontend uses a small API client with configurable and local fallback
origins. During local development, Vite proxies `/api` requests to the
backend at `http://127.0.0.1:5165`.

## Tech stack

### Frontend

- React 19
- TypeScript 6
- Vite 8
- Tailwind CSS 4 with the Vite plugin
- ESLint with TypeScript and React Hooks rules
- Native browser audio playback and speech fallback behavior

### Backend

- ASP.NET Core on .NET 10
- Minimal APIs and ASP.NET Core controllers
- Nullable reference types and implicit usings enabled
- OpenAPI metadata in development
- CORS enabled for the frontend

### Azure services and SDKs

- **Azure OpenAI** - Generates catch-up stories, personalized summaries, and
  event explanations.
- **Azure AI Speech** - Synthesizes English and Spanish neural audio.
- **Azure Blob Storage** - Stores and supplies the match dataset.
- **Microsoft Entra ID / Azure Identity** - Provides token-based access to Azure
  services during local development and deployment.

## API surface

The backend exposes the following primary routes:

| Method | Route                                        | Purpose                                             |
| ------ | -------------------------------------------- | --------------------------------------------------- |
| `GET`  | `/api/v1/matches`                            | List available matches                              |
| `GET`  | `/api/v1/matches/{matchId}`                  | Get match state, optionally at `?minute=`           |
| `POST` | `/api/v1/matches/{matchId}/simulation/reset` | Restart simulation, optionally with `?startMinute=` |
| `GET`  | `/api/v1/matches/{matchId}/timeline`         | Get events through `?upToMinute=`                   |
| `GET`  | `/api/v1/matches/{matchId}/insights`         | Get generated key-moment insights                   |
| `GET`  | `/api/v1/matches/{matchId}/possession`       | Get time-sliced possession                          |
| `POST` | `/api/v1/catchup`                            | Generate a missed-window recap                      |
| `POST` | `/api/v1/personalized-summary`               | Generate a personalized match story                 |
| `POST` | `/api/v1/explain`                            | Explain a selected timeline event                   |
| `POST` | `/api/v1/audio`                              | Return a generated audio data URL                   |
| `POST` | `/api/v1/audio/stream`                       | Return an MP3 audio stream                          |

In development, the backend also exposes:

- `GET /` - Health-style service response listing the main routes.
- `GET /openapi/v1.json` - OpenAPI document.
- `GET /test` - Interactive browser page for trying AI and audio endpoints.

## Configuration

Backend configuration is read from `appsettings.json`,
`appsettings.Development.json`, environment variables, and the normal ASP.NET
Core configuration providers. The important sections are:

- `AzureBlob` - Blob endpoint, container, and match blob name.
- `AzureOpenAI` - Azure OpenAI endpoint and model deployment name.
- `AzureSpeech` - Speech endpoint, region, resource identity, voice names, and
  synthesis timeout.
- `Simulation` - Simulated seconds per match minute.

Use environment variables or user secrets for sensitive values. Do not add
API keys or private connection strings to tracked configuration files.

## Repository layout

```text
NETUnpaused90/
  Endpoints/       Match, AI, and audio route mappings
  Models/          Match and request/response records
  Services/        Match, Azure OpenAI, Azure Speech, and identity services
  Program.cs       Dependency injection and HTTP pipeline

react-unpaused-90/
  src/components/  Broadcast dashboard and reusable UI components
  src/screens/     Dashboard screen and orchestration
  src/services/    Frontend API client
  src/languages/   English and Spanish translations
  src/Animations/  Match-event animations
  src/types/       Shared frontend data contracts
```
