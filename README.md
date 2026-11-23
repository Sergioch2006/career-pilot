# CareerPilot Local

**CareerPilot Local** is a local-first, privacy-focused, and LLM-provider-agnostic AI platform designed to manage professional profiles, analyze job offers against real-world market data, and generate tailored ATS resumes and executive interview preparation guides.

Built with **.NET 10**, **React + TypeScript**, **Model Context Protocol (MCP)**, and **local RAG (`SQLite` + `sqlite-vec`)**, the project demonstrates production-grade AI engineering patterns—including provider-neutral contracts, automated failure recovery, token/cost observability, and automated evaluation gates.

## Key Features

- **Living Professional Profile & Document Ingestion:** Import resumes via PDF, DOCX, or plain text with automatic metadata extraction (name, email, structured experience). Manage multiple profiles with full cascade updates and deletions.
- **Adaptive Profile Interviewer (Persistent Q&A):** An AI interviewer identifies gaps in your resume and asks targeted questions, persisting answers and indexing them into a local vector store (`sqlite-vec`) to enrich future analyses.
- **MCP-Powered Job-Fit & Market Analysis:** Connects to external **Model Context Protocol (MCP)** servers (Playwright, Brave/Tavily Search) to scrape job postings, research company mission/culture, and benchmark similar roles across job boards—returning a match score, strengths, and a targeted study plan.
- **Cascade Re-Analysis:** Updating a profile automatically refreshes local embeddings and asynchronously re-evaluates all tracked job offers to reflect your latest skills and closed gaps.
- **Executive Interview Guides & ATS Resume Generator:** Export print-ready PDF interview cheat sheets (with visual-anchor formatting and strategic questions) and single-column, ATS-optimized resumes (PDF/DOCX) with on-demand injection of selected project milestones.
- **Bilingual UI & Prompt Localization:** Instant `PT-br` and `EN-us` switching powered by `react-i18next`, propagating active language preferences to backend AI prompts.

## Architecture & Engineering Highlights

- **Backend:** ASP.NET Core Web API (.NET 10), Entity Framework Core, SQLite + `sqlite-vec`.
- **Frontend:** React, TypeScript, Vite, Tailwind CSS, `react-i18next`.
- **Provider-Agnostic AI:** Built on `Microsoft.Extensions.AI` (`IChatClient` / `IEmbeddingGenerator`) for seamless switching between **Gemini**, **OpenAI**, **Anthropic**, and local models (**Ollama**).
- **Agentic Tooling:** Native C# **MCP Client** orchestrating web automation, search, and filesystem tools.
- **Document Processing:** `PdfPig` and `DocumentFormat.OpenXml` for extraction; `Playwright` for HTML-to-PDF rendering.
- **Platform Resilience & Observability:**
  - **Failure Recovery:** `Polly` (`Microsoft.Extensions.Http.Resilience`) for exponential retries, circuit breakers, and multi-LLM fallbacks.
  - **Telemetry & Cost Tracking:** `OpenTelemetry` instrumentation logging latency, token usage, and estimated cost per analysis.
  - **Evaluation Gates:** Automated `xUnit` test suite validating structured JSON outputs and scoring consistency.

 ## Getting Started

### Prerequisites

Make sure you have the following installed on your machine:

- [Git](https://git-scm.com/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js (v20+ LTS)](https://nodejs.org/) *(required for the React frontend and running MCP servers via `npx`)*

### 1. Clone the Repository

```bash
git clone [https://github.com/Sergioch2006/career-pilot.git](https://github.com/Sergioch2006/career-pilot.git)
cd career-pilot
````

### 2. Configure the AI Provider

Create a src/backend/appsettings.Development.json file (ignored by Git) and configure your preferred LLM provider (Gemini, OpenAI, Anthropic, or Ollama):

````
{
  "AI": {
    "Provider": "Gemini",
    "ApiKey": "YOUR_API_KEY_HERE",
    "Model": "gemini-2.5-pro",
    "FallbackProvider": {
      "Provider": "OpenAI",
      "ApiKey": "YOUR_FALLBACK_API_KEY_HERE",
      "Model": "gpt-4o"
    }
  }
}
````

### 3. Configure MCP Servers

Create a src/backend/mcp-servers.json file (ignored by Git) to enable web scraping, market search, and optional local file exports:

````
{
  "mcpServers": {
    "playwright": {
      "command": "npx",
      "args": ["-y", "@playwright/mcp@latest"]
    },
    "brave-search": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-brave-search"],
      "env": {
        "BRAVE_API_KEY": "YOUR_BRAVE_API_KEY_HERE"
      }
    },
    "filesystem": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-filesystem",
        "/path/to/your/interviews/folder"
      ]
    }
  }
}
````

### 4. Run the Backend (.NET 10)

Open a terminal and start the ASP.NET Core Web API (this will automatically initialize the local SQLite database and apply migrations):

````
cd src/backend
dotnet restore
dotnet run
````

### 5. Run the Frontend (React + Vite)

Open a second terminal, install dependencies, and start the development server:

````
cd src/frontend
npm install
npm run dev
````

### 6. Run Evaluation Gates (Optional)

To execute the automated xUnit evaluation suite validating JSON schemas and scoring consistency:

````
dotnet test tests/CareerPilot.Evals
````
