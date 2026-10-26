# BuzzerGames

Making our favorite game shows playable, on phones, with a big screen in the middle of the room.

Right now there is one game, **Jeopardy**. The site opens on a "pick a game" page, with Wheel of Fortune,
Classic Concentration, Super Password, and The Price Is Right waiting in the wings.

## How a Jeopardy game works

- One **emcee** and three **players**. Everyone joins from the same web address.
- The **big screen** (a TV or projector) opens the site, creates a game, and shows the QR code.
  Phones scan the QR code or type the four-letter room code.
- The landing page has three choices: **Join as Emcee**, **Join as Player**, and (once the table is full) **Start Game**.
  A device that never joins, like the TV, turns into the game board when the game starts.
- Contestants call out a category and amount. The **emcee taps that clue** on their phone, and the clue appears on the big screen.
- Buzzers are open the whole time the clue is showing. **First buzz wins.**
- The emcee sees the correct answer and taps **Correct** or **Wrong**.
  Wrong costs points and the clue goes live again for everyone else.
- If nobody knows it, the emcee taps **reveal the answer**. Whoever picked last keeps control.
- Whoever answered last correctly picks the next clue.
- Two rounds, Daily Doubles (1 in round one, 2 in round two), and Final Jeopardy with wagers and typed answers.

## Run it on your computer

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/BuzzerGames.Web
```

Then open <http://localhost:5080>. To try all four roles by yourself, open the site in one normal window and
a few private/incognito windows (each one remembers its own seat).

## Run the checks

```bash
dotnet run --project tests/BuzzerGames.Core.Checks   # the rules of the game, no browser needed
dotnet run --project tests/BuzzerGames.Web.Checks    # draws every screen and checks what it shows
```

Both print a pass/fail line per check and exit with a non-zero code if anything fails.

## Deploy to Railway

1. In Railway: **New Project → Deploy from GitHub repo**, and pick this repo. Railway finds the `Dockerfile` by itself.
2. Open the service's **Settings → Networking → Generate Domain** to get a public address.
3. Optional: set the health check path to `/healthz`.
4. Keep it at **one replica**. Games live in the server's memory, so a second copy of the server wouldn't know about
   the first copy's games.

Things to know:

- A redeploy or restart wipes any game in progress. Don't push changes during the party.
- Phones keep their seat in the browser, so locking the screen or refreshing doesn't lose it.
- The QR code is drawn in the browser from the page's own address, so it just works on whatever domain you end up with.

## Where things live

```
src/BuzzerGames.Core/          The game itself: plain C#, no web code
  Jeopardy/JeopardyGame.cs       every rule: lobby, buzzing, judging, Daily Doubles, Final Jeopardy
  Jeopardy/GameData.cs           the shape of a game's questions
  Jeopardy/IGameDataSource.cs    "where do questions come from?" (swap this to change them)
  Jeopardy/DummyGameDataSource.cs  placeholder questions for testing
  Jeopardy/Snapshots.cs          frozen pictures of the game that pages draw from
  Jeopardy/GameRoomManager.cs    all running games, found by room code

src/BuzzerGames.Web/           The website
  Program.cs                     startup and wiring
  Games/GameCatalog.cs           the "pick a game" list
  Components/Pages/              Home, JeopardyHome, JeopardyRoom (the one page every screen goes through)
  Components/Jeopardy/           LobbyView, BoardView (TV), EmceeView, PlayerView, ScoreStrip
  wwwroot/app.css, js/bg.js      styling, and the small JavaScript bits (saved seat, QR code)

tests/                         Checks that play real games and verify the rules and the screens
Dockerfile                     How Railway builds and runs it
```

## What's next

- Swap the dummy questions for a real dataset (the engine only knows about `IGameDataSource`).
- Postgres, to remember which games have been played so none repeat.
- The next game show.
